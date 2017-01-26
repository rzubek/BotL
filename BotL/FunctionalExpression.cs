﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FunctionalExpression.cs" company="Ian Horswill">
// Copyright (C) 2017 Ian Horswill
//  
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in the
// Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so, subject to the
// following conditions:
//  
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using BotL.Unity;
using static BotL.Engine;

namespace BotL
{
    internal static class FunctionalExpression
    {
        public const ushort EvalStackOffset = 256;

        /// <summary>
        /// Execute the compiled functional express starting at pc inside of clause.
        /// Uses the area of the data stack starting EvalStackOffset entries after dTop.
        /// On exit, the result will be in Engine.DataStack[dTop+EvalStackOffset]
        /// </summary>
        /// <param name="p">Predicate being executed (for access to constant tables)</param>
        /// <param name="clause">Compiled clause code</param>
        /// <param name="frameBase">Base address of clause's environment in DataStack</param>
        /// <param name="pc">PC within clause</param>
        /// <param name="stack">top of the data stack</param>
        /// <returns>PC of the next instruction after the functional expression</returns>
        public static ushort Eval(Predicate p, byte[] clause, ushort pc, ushort frameBase, ushort stack)
        {
            // The top of the stack is actually at 
            stack += EvalStackOffset;

            while (true)
            {
                switch ((FOpcode) clause[pc++])
                {
                    case FOpcode.Return:
                        return pc;

                    case FOpcode.PushSmallInt:
                        DataStack[stack++].Set((sbyte) clause[pc++]);
                        break;

                    case FOpcode.PushInt:
                        DataStack[stack++].Set(p.GetIntConstant(clause[pc++]));
                        break;

                    case FOpcode.PushFloat:
                        DataStack[stack++].Set(p.GetFloatConstant(clause[pc++]));
                        break;

                    case FOpcode.PushBoolean:
                        DataStack[stack++].Set(clause[pc++] != 0);
                        break;

                    case FOpcode.PushObject:
                        DataStack[stack++].SetReference(p.GetObjectConstant<object>(clause[pc++]));
                        break;

                    case FOpcode.Load:
                        var address = Deref(frameBase + clause[pc++]);
                        if (DataStack[address].Type == TaggedValueType.Unbound)
                            throw new InstantiationException("Uninstantiated variable in functional expression");
                        DataStack[stack++] = DataStack[address];
                        break;

                    case FOpcode.LoadGlobal:
                        var globalVariable = (GlobalVariable)p.GetObjectConstant<object>(clause[pc++]);
                        DataStack[stack++] = globalVariable.Value;
                        break;

                    case FOpcode.Add:
                    {
                        var op2Addr = --stack;
                        var op1Addr = --stack;
                        if (BothInts(op1Addr, op2Addr))
                            DataStack[stack++].Set(DataStack[op1Addr].integer +
                                                          DataStack[op2Addr].integer);
                        else
                            DataStack[stack++].Set(DataStack[op1Addr].AsFloat +
                                                          DataStack[op2Addr].AsFloat);
                    }
                        break;

                    case FOpcode.Subtract:
                    {
                        var op2Addr = --stack;
                        var op1Addr = --stack;
                        if (BothInts(op1Addr, op2Addr))
                            DataStack[stack++].Set(DataStack[op1Addr].integer -
                                                          DataStack[op2Addr].integer);
                        else
                            DataStack[stack++].Set(DataStack[op1Addr].AsFloat -
                                                          DataStack[op2Addr].AsFloat);
                    }
                        break;

                    case FOpcode.Negate:
                        {
                            var op1Addr = --stack;
                            if (DataStack[op1Addr].Type == TaggedValueType.Integer)
                                DataStack[stack++].Set(-DataStack[op1Addr].integer);
                            else
                                DataStack[stack++].Set(DataStack[op1Addr].floatingPoint);
                        }
                        break;

                    case FOpcode.Multiply:
                    {
                        var op2Addr = --stack;
                        var op1Addr = --stack;
                        if (BothInts(op1Addr, op2Addr))
                            DataStack[stack++].Set(DataStack[op1Addr].integer *
                                                          DataStack[op2Addr].integer);
                        else
                            DataStack[stack++].Set(DataStack[op1Addr].AsFloat *
                                                          DataStack[op2Addr].AsFloat);
                    }
                        break;

                    case FOpcode.Divide:
                    {
                        var op2Addr = --stack;
                        var op1Addr = --stack;
                        DataStack[stack++].Set(DataStack[op1Addr].AsFloat /
                                                      DataStack[op2Addr].AsFloat);
                    }
                        break;

                    case FOpcode.FieldReference:
                    {
                        var fieldName = (string) DataStack[--stack].reference;
                        var target = DataStack[--stack].Value;
                        var result = target.GetPropertyOrField(fieldName);
                        DataStack[stack++].SetGeneral(result);
                    }
                        break;

                    case FOpcode.MethodCall:
                    {
                        object[] args = new object[clause[pc++]];
                        for (var i = args.Length-1; i >= 0; i--)
                            args[i] = DataStack[--stack].Value;
                        var methodName = (string) DataStack[--stack].reference;
                        var target = DataStack[--stack].Value;
                        var result = target.InvokeMethod(methodName, args);
                        DataStack[stack++].SetGeneral(result);
                    }
                        break;

                    case FOpcode.Constructor:
                        {
                            object[] args = new object[clause[pc++]];
                            for (var i = args.Length - 1; i >= 0; i--)
                                args[i] = DataStack[--stack].Value;
                            var type = (Type)DataStack[--stack].reference;
                            var result = type.CreateInstance(args);
                            DataStack[stack++].SetGeneral(result);
                        }
                        break;

                    case FOpcode.ComponentLookup:
                    {
                        stack = UnityUtilities.LookupUnityComponent(stack);
                    }
                        break;

                    case FOpcode.Array:
                    {
                        var result = new object[clause[pc++]];
                        for (int i = result.Length - 1; i >= 0; i--)
                            result[i] = DataStack[--stack].Value;
                        DataStack[stack++].SetReference(result);
                    }
                        break;

                    case FOpcode.ArrayList:
                        {
                            var result = new ArrayList(clause[pc++]);
                            for (int i = result.Count - 1; i >= 0; i--)
                                result[i] = DataStack[--stack].Value;
                            DataStack[stack++].SetReference(result);
                        }
                        break;

                    case FOpcode.Hashset:
                    {
                        var count = clause[pc++];
                        var result = new HashSet<object>();
                        for (int i = 0; i < count; i++)
                            result.Add(DataStack[--stack].Value);
                        DataStack[stack++].SetReference(result);
                    }
                        break;

                    case FOpcode.NonFalse:
                        // Push true on the stack if TOS is anything but the boolean False.
                    {
                        var tos = stack-1;
                        DataStack[tos].Set(DataStack[tos].Type != TaggedValueType.Boolean || DataStack[tos].boolean);
                    }
                        break;
                        
                    default:
                        throw new InvalidOperationException("Bad opcode in compiled functional expression");
                }
            }
        }

        private static bool BothInts(ushort op1Addr, ushort op2Addr)
        {
            return DataStack[op1Addr].Type == TaggedValueType.Integer &&
                   DataStack[op2Addr].Type == TaggedValueType.Integer;
        }
    }
}
