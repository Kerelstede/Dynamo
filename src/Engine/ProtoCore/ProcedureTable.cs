﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ProtoCore.AssociativeGraph;
using ProtoCore.AST.AssociativeAST;
using ProtoCore.Utils;

namespace ProtoCore.DSASM
{
    public enum ProcedureDistance
    {
        // TODO Jun: Corecion score may have to be expanded depending on specific types
        kCoerceScore = 10,
        kCoerceDoubleToIntScore = 5,
        kCoerceIntToDoubleScore = 30,
        kCoerceBaseClass = 40,
        kExactMatchScore = 50,
        kNotMatchScore = 0,
        kExactMatchDistance = 0,
        kMaxDistance = Int32.MaxValue,
        kInvalidDistance = -1
    }

    public struct ArgumentInfo
    {
        public string Name { get; set; }
        public bool IsDefault
        {
            get
            {
                return DefaultExpression != null;
            }
        }
        public ProtoCore.AST.Node DefaultExpression;
        public ProtoCore.AST.AssociativeAST.ExternalAttributes Attributes;
    }

    [System.Diagnostics.DebuggerDisplay("{name}, procId={procId}, classScope={classScope}")]
    public class ProcedureNode
    {
        public string name;                         // Name of the procedure
        public int pc;                              // First instruction of the procedure
        public int localCount;                      // Number of local variables
        public List<ProtoCore.Type> argTypeList;    // List of arguments required by the procedure
        public List<ArgumentInfo> argInfoList;      // List of arguments' information (default value)
        public ProtoCore.Type returntype;           // Procedure return data type
        public bool isConstructor;                  // Flag whether procedure is a constructor or not
        public bool isStatic;                       // Flag whether procedure is a static function or not
        public int runtimeIndex { get; set; }       // Index of the procedure at the runtime executable tables
        public int procId { get; set; }             // Index of the procedure in its procedure table
        public int classScope { get; set; }         // Index of the class that the procedure belongs to
        public int HashID { get; set; }             // The hash of the function given the name and argument type string
        public ProtoCore.CompilerDefinitions.AccessModifier access {get; set;}
		public List<AttributeEntry> Attributes { get; set; }		
        public bool isExternal { get; set; }
        public bool isAssocOperator { get; set; }
        public bool isAutoGenerated { get; set; }
        public bool isAutoGeneratedThisProc { get; set; }
        public bool isActive { get; set; }
        public MethodAttributes MethodAttribute { get; set; }
        public bool isVarArg { get; set; }
        public List<int> ChildCodeBlocks { get; set; }

        public Stack<UpdateNodeRef> updatedGlobals { get; set; }
        public Stack<UpdateNodeRef> updatedProperties { get; set; }
        public Dictionary<string, List<UpdateNodeRef>> updatedArgumentProperties { get; set; }
        public Dictionary<string, List<UpdateNode>> updatedArgumentArrays { get; set; }

        /// <summary>
        /// The list of graphnodes that this function owns
        /// </summary>
        public List<GraphNode> GraphNodeList { get; private set; }

        public ProcedureNode()
        {
            procId = DSASM.Constants.kInvalidIndex;
            classScope = DSASM.Constants.kInvalidIndex;
            argTypeList = new List<Type>();
            argInfoList = new List<ArgumentInfo>();
            isConstructor = false;
            isStatic = false;
            access = CompilerDefinitions.AccessModifier.kPublic;
            isAutoGenerated = false;
            isAutoGeneratedThisProc = false;
            isActive = true;
            ChildCodeBlocks = new List<int>();

            updatedGlobals = new Stack<AssociativeGraph.UpdateNodeRef>();
            updatedProperties = new Stack<AssociativeGraph.UpdateNodeRef>();
            updatedArgumentProperties = new Dictionary<string, List<AssociativeGraph.UpdateNodeRef>>();
            updatedArgumentArrays = new Dictionary<string, List<AssociativeGraph.UpdateNode>>();
            GraphNodeList = new List<GraphNode>();
        }


        public ProcedureNode(ProcedureNode rhs)
        {
            name = rhs.name;
            pc = rhs.pc;
            localCount = rhs.localCount;
            returntype = rhs.returntype;
            runtimeIndex = rhs.runtimeIndex;
            isExternal = rhs.isExternal;
            isAssocOperator = rhs.isAssocOperator;
            isVarArg = rhs.isVarArg;

            procId = rhs.procId;
            classScope = rhs.classScope;
            argTypeList = new List<Type>(rhs.argTypeList);
            argInfoList = new List<ArgumentInfo>(rhs.argInfoList);
            isConstructor = rhs.isConstructor;
            isStatic = rhs.isStatic;
            access = rhs.access;
            isAutoGenerated = rhs.isAutoGenerated;
            isAutoGeneratedThisProc = rhs.isAutoGeneratedThisProc;
            isActive = rhs.isActive;
            ChildCodeBlocks = new List<int>(rhs.ChildCodeBlocks);

            // Runtime properties are initialized
            updatedGlobals = new Stack<AssociativeGraph.UpdateNodeRef>();
            updatedProperties = new Stack<AssociativeGraph.UpdateNodeRef>();
            updatedArgumentProperties = new Dictionary<string, List<AssociativeGraph.UpdateNodeRef>>();
            updatedArgumentArrays = new Dictionary<string, List<AssociativeGraph.UpdateNode>>();
            GraphNodeList = new List<GraphNode>();
        }

        public override bool Equals(object obj)
        {
            var rhs = obj as ProcedureNode;
            if (rhs == null)
            {
                return false;
            }

            return procId == rhs.procId && 
                   classScope == rhs.classScope && 
                   localCount == rhs.localCount && 
                   name.Equals(rhs.name);
        }
    }

    public class ProcedureTable
    {
        public int runtimeIndex { get; private set; }
        public List<ProcedureNode> procList { get; set; }
        
        public ProcedureTable(int runtimeindex)
        {
            runtimeIndex = runtimeindex;
            procList = new List<ProcedureNode>();
        }

        public ProcedureTable(ProcedureTable rhs)
        {
            this.runtimeIndex = rhs.runtimeIndex;
            this.procList = new List<ProcedureNode>();
            for (int n = 0; n < rhs.procList.Count; ++n)
            {
                procList.Add(new ProcedureNode(rhs.procList[n]));
            }
        }

        public int Append(ProcedureNode node)
        {
            int index = IndexOfExact(node.name, node.argTypeList, node.isAutoGeneratedThisProc);
            if (Constants.kInvalidIndex == index)
            {
                procList.Add(node);
                node.procId = procList.Count - 1;
                return node.procId;
            }
            else
            {
                var procNode = procList[index];
                if (!procNode.isActive)
                {
                    procList[index] = node;
                    node.procId = index;
                    return node.procId;
                }
            }
            return ProtoCore.DSASM.Constants.kInvalidIndex;
        }

        /// <summary>
        /// Return all functions whose names are 'name'
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public IEnumerable<ProcedureNode> GetFunctionsBy(string name)
        {
            return procList.Where(p => p.name.Equals(name));
        }

        /// <summary>
        /// Return all functions whose names are 'name' and the number of
        /// parameters are 'argumentNumber'
        /// </summary>
        /// <param name="name"></param>
        /// <param name="argumentNumber"></param>
        /// <returns></returns>
        public IEnumerable<ProcedureNode> GetFunctionsBy(string name, int argumentNumber)
        {
            return procList.Where(p =>
            {
                int defaultArgumentNumber = p.argInfoList.Count(X => X.DefaultExpression != null);
                int parameterNumber = p.argTypeList.Count;
                return p.name.Equals(name) &&
                       (argumentNumber <= parameterNumber) &&
                       (argumentNumber - parameterNumber <= defaultArgumentNumber);
            }).OrderBy(p => p.argTypeList.Count - argumentNumber);
        }
        
        public ProcedureNode GetFirst(string name, int argCount)
        {
            ProcedureNode procReturn = null;
            for (int n = 0; n < procList.Count; ++n)
            {
                if (name == procList[n].name && argCount == procList[n].argTypeList.Count)
                {
                    procReturn = procList[n];
                    break;
                }
            }
            return procReturn;
        }

        public ProcedureNode GetFirstStatic(string name)
        {
            ProcedureNode procReturn = null;
            for (int n = 0; n < procList.Count; ++n)
            {
                if (name == procList[n].name && procList[n].isStatic)
                {
                    procReturn = procList[n];
                    break;
                }
            }
            return procReturn;
        }

        public int IndexOf(string name, List<ProtoCore.Type> argTypeList, bool isStaticOrConstructor = false)
        {
            int currentProcedure = ProtoCore.DSASM.Constants.kInvalidIndex;
            bool functionPointerCheck = argTypeList == null;

            if (functionPointerCheck) // check for function pointer
            {
                for (int n = 0; n < procList.Count; ++n)
                {
                    if (name == procList[n].name)
                    {
                        currentProcedure = n;
                        break;
                    }
                }
                return currentProcedure;
            }

            // how many default parameters are used
            int defaultParamNum = Int32.MaxValue;

            for (int n = 0; n < procList.Count; ++n)
            {
                var proc = procList[n];
                if (proc.isAutoGeneratedThisProc)
                    continue;

                var argNum = proc.argTypeList.Count;
                var paramNum = argTypeList.Count;
                int defaultArgNum = proc.argInfoList.Count(X => X.DefaultExpression != null);

                if (name.Equals(proc.name) && (argNum >= paramNum) && (argNum - paramNum <= defaultArgNum))
                {
                    if (!isStaticOrConstructor ||
                        (isStaticOrConstructor && (proc.isStatic || proc.isConstructor)))
                    {
                        var num = argNum - paramNum;
                        if (num < defaultParamNum)
                        {
                            defaultParamNum = num;
                            currentProcedure = n;
                        }

                        if (defaultParamNum == 0)
                        {
                            break;
                        }
                    }
                }
            }
            return currentProcedure;
        }

        public int IndexOfExact(string name, List<ProtoCore.Type> args, bool isAutoGeneratedThisProc)
        {
            // This functions attempts to find an exact match and return its index
            // Iterate through all defined functions
            for (int n = 0; n < procList.Count; ++n)
            {
                if (name == procList[n].name && 
                    args.Count == procList[n].argTypeList.Count &&
                    procList[n].isAutoGeneratedThisProc == isAutoGeneratedThisProc)
                {
                    bool isMatch = true;
                    for (int i = 0; i < args.Count; ++i)
                    {
                        if ((procList[n].argTypeList[i].UID != args[i].UID) ||
                            (procList[n].argTypeList[i].IsIndexable != args[i].IsIndexable) ||
                            (procList[n].argTypeList[i].rank != args[i].rank))
                        {
                            isMatch = false;
                            break;
                        }
                    }

                    if (isMatch)
                    {
                        return n;
                    }
                }
            }
            return ProtoCore.DSASM.Constants.kInvalidIndex;
        }

        public int IndexOfFirst(string name)
        {
            for (int n = 0; n < procList.Count; ++n)
            {
                if (name == procList[n].name)
                {
                    return n;
                }
            }
            return ProtoCore.DSASM.Constants.kInvalidIndex;
        }

        public int IndexOfHash(int hash)
        {
            for (int n = 0; n < procList.Count; ++n)
            {
                if (hash == procList[n].HashID)
                {
                    return n;
                }
            }
            return ProtoCore.DSASM.Constants.kInvalidIndex;
        }

        /// <summary>
        /// This sets the procedure node to be inactive by modifiying its name
        /// </summary>
        /// <param name="name"></param>
        /// <param name="args"></param>
        public void SetInactive(int index)
        {
            if (index < 0 || index >= procList.Count)
                throw new ArgumentOutOfRangeException();
            procList[index].isActive = false;
        }
    }
}