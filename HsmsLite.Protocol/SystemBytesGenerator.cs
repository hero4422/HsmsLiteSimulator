using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HsmsLite.Protocol
{
    // 요청/응답 상관관계 ID
    /// <summary>Thread-safe generator for the 4-byte "System Bytes" transaction id that correlates
    /// a .req with its matching .rsp.</summary>
    public sealed class SystemBytesGenerator
    {
        private int _counter;

        public uint Next() => unchecked((uint)Interlocked.Increment(ref _counter));
    }
}
