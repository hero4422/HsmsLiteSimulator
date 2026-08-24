using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HsmsLite.Protocol
{
    // 메시지 타입 정의
    /// <summary>
    /// HSMS control message types (SEMI E37 "Generic Services"), carried in header byte 5 (SType).
    /// SType 0 is reserved for Data Messages (SECS-II payloads); the rest are session-management
    /// control messages used to bring a TCP connection up to the "Selected" state and tear it down.
    /// </summary>
    public enum HsmsSType : byte
    {
        DataMessage = 0,
        SelectReq = 1,
        SelectRsp = 2,
        DeselectReq = 3,
        DeselectRsp = 4,
        LinktestReq = 5,
        LinktestRsp = 6,
        RejectReq = 7,
        SeparateReq = 9,
    }

    /// <summary>
    /// Select.rsp / Deselect.rsp status codes (header byte 4, "Select Status"). 0 = accepted;
    /// non-zero values report the specific reason the peer refused the request.
    /// </summary>
    public enum HsmsSelectStatus : byte
    {
        Ok = 0,
        AlreadyActive = 1,
        NotReady = 2,
        Exhausted = 3,
        NotSelected = 4,
    }
}
