using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HsmsLite.Protocol
{
    // 연결 상태 3단계
    /// <summary>
    /// The three HSMS connection states from SEMI E37's connection state diagram. This "lite"
    /// implementation collapses the standard's NOT-CONNECTED sub-states (which mostly deal with
    /// active-vs-passive TCP retry timers, T5) into a single NotConnected state, since that detail
    /// isn't needed to demonstrate the Select/Selected/Separate lifecycle.
    /// </summary>
    public enum HsmsConnectionState
    {
        /// <summary>No TCP connection established yet.</summary>
        NotConnected,

        /// <summary>TCP connection is up, but Select.req/rsp has not completed - only control
        /// messages (Select, Linktest, Separate) are valid here; a Data Message here is a protocol error.</summary>
        Connected,

        /// <summary>Select.req/rsp completed successfully - Data Messages may now be exchanged.</summary>
        Selected,
    }
}
