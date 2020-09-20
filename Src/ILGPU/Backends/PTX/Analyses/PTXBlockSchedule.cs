// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2020 Marcel Koester
//                                    www.ilgpu.net
//
// File: PTXBlockSchedule.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Analyses.ControlFlowDirection;
using ILGPU.IR.Analyses.TraversalOrders;
using ILGPU.IR.Values;
using ILGPU.Util;
using System;
using System.Runtime.CompilerServices;
using PTXBlockCollection = ILGPU.IR.BasicBlockCollection<
    ILGPU.IR.Analyses.TraversalOrders.PreOrder,
    ILGPU.IR.Analyses.ControlFlowDirection.Forwards>;

namespace ILGPU.Backends.PTX.Analyses
{
    /// <summary>
    /// Represents a PTX-specific block schedule.
    /// </summary>
    public readonly struct PTXBlockSchedule
    {
        #region Nested Types

        /// <summary>
        /// A specific successor provider that inverts the successors of all
        /// <see cref="IfBranch"/> terminators.
        /// </summary>
        private readonly struct SuccessorProvider :
            ITraversalSuccessorsProvider<Forwards>
        {
            /// <summary>
            /// Returns all successors in the default order except for
            /// <see cref="IfBranch"/> terminators. The successors of these terminators
            /// will be reversed to invert all if branch targets.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly ReadOnlySpan<BasicBlock> GetSuccessors(BasicBlock basicBlock)
            {
                var successors = basicBlock.Successors;
                if (basicBlock.Terminator is IfBranch ifBranch && ifBranch.IsInverted)
                {
                    var tempList = successors.ToInlineList();
                    tempList.Reverse();
                    successors = tempList;
                }
                return successors;
            }
        }

        #endregion

        #region Static

        /// <summary>
        /// Creates a new block schedule using the given blocks.
        /// </summary>
        /// <typeparam name="TOrder">The current order.</typeparam>
        /// <typeparam name="TDirection">The control-flow direction.</typeparam>
        /// <param name="blocks">The input blocks.</param>
        /// <returns>The created block schedule.</returns>
        public static PTXBlockSchedule CreateSchedule<TOrder, TDirection>(
            in BasicBlockCollection<TOrder, TDirection> blocks)
            where TOrder : struct, ITraversalOrder
            where TDirection : struct, IControlFlowDirection =>
            new PTXBlockSchedule(blocks.ChangeOrder<PreOrder, Forwards>());

        /// <summary>
        /// Creates a schedule from an already existing schedule.
        /// </summary>
        /// <typeparam name="TOrder">The current order.</typeparam>
        /// <typeparam name="TDirection">The control-flow direction.</typeparam>
        /// <param name="blocks">The input blocks.</param>
        /// <returns>The created block schedule.</returns>
        public static PTXBlockSchedule UseSchedule<TOrder, TDirection>(
            in BasicBlockCollection<TOrder, TDirection> blocks)
            where TOrder : struct, ITraversalOrder
            where TDirection : struct, IControlFlowDirection =>
            new PTXBlockSchedule(
                blocks.Traverse<PreOrder, Forwards, SuccessorProvider>(default));

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new PTX block schedule.
        /// </summary>
        /// <param name="blocks">The underlying block collection.</param>
        private PTXBlockSchedule(in PTXBlockCollection blocks)
        {
            Blocks = blocks;
            BlockIndices = blocks.CreateMap(new BasicBlockMapTraversalIndexProvider());
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns all blocks.
        /// </summary>
        public PTXBlockCollection Blocks { get; }

        /// <summary>
        /// Returns the block-traversal indices.
        /// </summary>
        public BasicBlockMap<int> BlockIndices { get; }

        /// <summary>
        /// Maps the given block to its traversal index.
        /// </summary>
        /// <param name="block">The block to map to its traversal index.</param>
        /// <returns>The associated traversal index.</returns>
        public readonly int this[BasicBlock block] => BlockIndices[block];

        #endregion

        #region Methods

        /// <summary>
        /// Returns true if the given <paramref name="successor"/> is an implicit
        /// successor of the <paramref name="source"/> block.
        /// </summary>
        /// <param name="source">The source block.</param>
        /// <param name="successor">The target successor to jump to.</param>
        /// <returns>True, if the given successor in an implicit branch target.</returns>
        public readonly bool IsImplicitSuccessor(
            BasicBlock source,
            BasicBlock successor) =>
            this[source] + 1 == this[successor];

        /// <summary>
        /// Returns true if the given block needs an explicit branch target.
        /// </summary>
        /// <param name="block">The block to test.</param>
        /// <returns>True, if the given block needs an explicit branch target.</returns>
        public readonly bool NeedBranchTarget(BasicBlock block)
        {
            // If there is more than one predecessor
            if (block.Predecessors.Length > 1)
                return true;

            // If there is less than one predecessor
            if (block.Predecessors.Length < 1)
                return false;

            // If there is exactly one predecessor, we have to check whether this block
            // can be reached via an implicit successor branch
            var pred = block.Predecessors[0];
            if (pred.Terminator is IfBranch ifBranch)
            {
                var (trueTarget, _) = ifBranch.GetNotInvertedBranchTargets();
                return block != trueTarget || !IsImplicitSuccessor(pred, block);
            }

            // Ensure that we are not removing labels from switch-based branch targets
            return !(pred.Terminator is UnconditionalBranch);
        }

        /// <summary>
        /// Returns an enumerator to iterate over all blocks in the underlying
        /// collection.
        /// </summary>
        public readonly PTXBlockCollection.Enumerator GetEnumerator() =>
            Blocks.GetEnumerator();

        #endregion
    }

    /// <summary>
    /// Extensions methods for the <see cref="PTXBlockSchedule"/> structure.
    /// </summary>
    public static class PTXBlockScheduleExtensions
    {
        /// <summary>
        /// Creates a new block schedule using the given blocks.
        /// </summary>
        /// <typeparam name="TOrder">The current order.</typeparam>
        /// <typeparam name="TDirection">The control-flow direction.</typeparam>
        /// <param name="blocks">The input blocks.</param>
        /// <returns>The created block schedule.</returns>
        public static PTXBlockSchedule CreatePTXBlockSchedule<
            TOrder,
            TDirection>(
            this BasicBlockCollection<TOrder, TDirection> blocks)
            where TOrder : struct, ITraversalOrder
            where TDirection : struct, IControlFlowDirection =>
            PTXBlockSchedule.CreateSchedule(blocks);

        /// <summary>
        /// Creates a schedule from an already existing schedule.
        /// </summary>
        /// <typeparam name="TOrder">The current order.</typeparam>
        /// <typeparam name="TDirection">The control-flow direction.</typeparam>
        /// <param name="blocks">The input blocks.</param>
        /// <returns>The created block schedule.</returns>
        public static PTXBlockSchedule UsePTXBlockSchedule<
            TOrder,
            TDirection>(
            this BasicBlockCollection<TOrder, TDirection> blocks)
            where TOrder : struct, ITraversalOrder
            where TDirection : struct, IControlFlowDirection =>
            PTXBlockSchedule.UseSchedule(blocks);
    }
}
