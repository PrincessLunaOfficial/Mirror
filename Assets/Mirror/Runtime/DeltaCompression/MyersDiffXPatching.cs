// helper class to convert MyersDiff result to binary patch.
// and to apply binary patch to another binary.
// this is framework dependent, so not part of MyersDiffX repo.
using System;
using System.Collections.Generic;
using MyersDiffX;

namespace Mirror
{
    public static class MyersDiffXPatching
    {
        // helper function to convert Diff.Item[] to an actual patch
        public static void MakePatch(ArraySegment<byte> A, ArraySegment<byte> B, List<Item> diffs, NetworkWriter result)
        {
            // serialize diffs
            //   deletedA means: it was in A, it's deleted in B.
            //   insertedB means: it wasn't in A, it's added to B.
            Compression.CompressVarInt(result, (ulong)diffs.Count);
            foreach (Item change in diffs)
            {
                // assuming the other end already has 'A'
                // we need to save instructions to construct 'B' from 'A'.
                Compression.CompressVarInt(result, (ulong)change.StartA);
                Compression.CompressVarInt(result, (ulong)change.StartB);
                Compression.CompressVarInt(result, (ulong)change.deletedA);
                Compression.CompressVarInt(result, (ulong)change.insertedB);

                // need to provide the actual values that were inserted
                // it means compared to 'A' at 'StartA',
                // 'B' at 'startB' has 'N' the following new values
                for (int i = 0; i < change.insertedB; ++i)
                {
                    // DO NOT _VARINT_ the actual value.
                    // it's just a byte. it could be anything. we don't know.
                    result.WriteByte(B.Array[change.StartB + i]);
                }
            }
        }

        // TODO try reverse reconstruction from scratch instead of inserting/
        //      removing from 'B'. that would avoid LinkedList.
        public static void ApplyPatch(NetworkWriter A, NetworkReader delta, NetworkWriter result)
        {
            // the challenge here is to reconstruct B := A + Delta
            // AND do that without allocations.
            //
            // the easy solution is to duplicate A and apply all changes.
            // that's too slow though. need RemoveRange/Insert/duplications etc.
            //
            // let's try to reconstruct from scratch, directly into the result.
            //
            // for reference, here is a simple example:
            //   Delta(abc, aab) gives:
            //     item: startA=1 startB=1 deletedA=0 insertedB=1
            //     item: startA=2 startB=3 deletedA=1 insertedB=0
            //
            // applying a patch FORWARD, step by step:
            //     B := A
            //     B = abc
            //     we insert 1 value from A[StartA] at B[StartB]:
            //     B = aabc
            //     we delete 1 value that was at A[StartA] in B[StartB]:
            //     B = aab
            //
            // applying a patch FROM SCRATCH, step by step:
            //     B = ""
            //     first change is insert 1 value from A[StartA] at B[StartB]:
            //       copy A until StartA first:
            //         B = a
            //       insert the value from A[StartA] now:
            //         B = aa
            //     second change is delete 1 value from A[StartA] at B[StartB]:
            //       copy A until StartA first, from where we left of
            //         B = aab
            //       delete the value from A[StartA] now:
            //         means simply skip them in A

            ArraySegment<byte> ASegment = A.ToArraySegment();

            // read amount of changes in any case
            int count = (int)Compression.DecompressVarInt(delta);

            // any changes?
            if (count > 0)
            {
                // reconstruct...
                for (int i = 0; i < count; ++i)
                {
                    // read the next change
                    int StartA = (int)Compression.DecompressVarInt(delta);
                    int StartB = (int)Compression.DecompressVarInt(delta);
                    int deletedA = (int)Compression.DecompressVarInt(delta);
                    int insertedB = (int)Compression.DecompressVarInt(delta);

                    // we progressed through 'A' until 'IndexA'.
                    // copy everything until the next change at 'StartB'

                    // first of: copy everything until this change.
                    result.WriteBytes(ASegment.Array, ASegment.Offset + AIndex, StartB);

                    // so we are at

                    // deletedA means we don't take those from A.
                    // in other words, skip them.
                    // TODO safety. should be > 0 and within range etc.
                    AIndex += deletedA;

                    // inserted means we have 'N' new values in delta.
                    for (int n = 0; n < insertedB; ++n)
                    {
                        // DO NOT _VARINT_ the actual value.
                        // it's just a byte. it could be anything. we don't know.
                        byte value = delta.ReadByte();
                        result.WriteByte(value);
                        //Debug.Log($"->patch: inserted '0x{value:X2}' into B @ {StartB + n} => {BitConverter.ToString(B.ToArray())}");
                    }

                    //

                }
            }
            // no changes. simply copy A into result.
            // TODO this could be 'copy everything from last to finish
            else
            {
                result.WriteBytes(ASegment.Array, ASegment.Offset, ASegment.Count);
            }

            // convert A bytes to list for easier insertion/deletion
            // copy byte by byte to avoid new List(A.ToArray()) allocation.
            // TODO avoid List<byte> allocation
            // TODO linked list for performance? insert is expensive
            /*List<byte> B = new List<byte>();
            ArraySegment<byte> ASegment = A.ToArraySegment();
            for (int i = 0; i < ASegment.Count; ++i)
                B.Add(ASegment.Array[ASegment.Offset + i]);

            // deserialize patch
            int count = (int)Compression.DecompressVarInt(delta);
            // TODO safety..
            for (int i = 0; i < count; ++i)
            {
                // we only ever need (and serialize) StartB
                int StartB = (int)Compression.DecompressVarInt(delta);

                // deleted amount
                int deletedA = (int)Compression.DecompressVarInt(delta);

                // deletedA means: compared to A, 'N' were deleted in B at 'StartB'
                // TODO we need a linked list or similar data structure for perf
                B.RemoveRange(StartB, deletedA);

                // inserted amount
                int insertedB = (int)Compression.DecompressVarInt(delta);
                for (int n = 0; n < insertedB; ++n)
                {
                    // DO NOT _VARINT_ the actual value.
                    // it's just a byte. it could be anything. we don't know.
                    byte value = delta.ReadByte();
                    B.Insert(StartB + n, value);
                    //Debug.Log($"->patch: inserted '0x{value:X2}' into B @ {StartB + n} => {BitConverter.ToString(B.ToArray())}");
                }
            }

            // put B into result writer (nonalloc)
            for (int i = 0; i < B.Count; ++i)
                result.WriteByte(B[i]);*/
        }
    }
}
