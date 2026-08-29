using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HsmsLite.Protocol
{
    /// <summary>
    /// Tracks one HSMS connection's state and enforces the SEMI E37 connection-state rules:
    /// only Select.req / Linktest / Separate are legal before Selected, and once Selected,
    /// a second Select.req or a Data Message before Selected is a protocol violation.
    ///
    /// This is intentionally small and dependency-free so it can be unit-tested (see
    /// HsmsLite.Protocol.Tests) without spinning up real sockets.
    /// </summary>
    public sealed class HsmsStateMachine
    {
        public HsmsConnectionState State { get; private set; } = HsmsConnectionState.NotConnected;

        public event Action<HsmsConnectionState, HsmsConnectionState>? StateChanged;

        public void OnTcpConnected()
        {
            Transition(HsmsConnectionState.NotConnected, HsmsConnectionState.Connected);
        }

        /// <summary>Call when this side's Select.req was accepted (Select.rsp status = Ok) or when
        /// this side accepted a peer's Select.req.</summary>
        public void OnSelected()
        {
            Transition(HsmsConnectionState.Connected, HsmsConnectionState.Selected);
        }

        public void OnSeparatedOrDeselected()
        {
            if (State == HsmsConnectionState.Selected)
                Transition(HsmsConnectionState.Selected, HsmsConnectionState.Connected);
        }

        public void OnTcpDisconnected()
        {
            if (State == HsmsConnectionState.NotConnected)
                return; // already torn down (e.g. Separate.req handling already called this) - no-op

            var previous = State;
            State = HsmsConnectionState.NotConnected;
            StateChanged?.Invoke(previous, State);
        }

        /// <summary>
        /// Validates that receiving/sending a message of the given type is legal in the current
        /// state. Throws <see cref="HsmsProtocolException"/> otherwise, so protocol violations are
        /// caught immediately instead of silently corrupting session state.
        /// </summary>
        public void AssertValid(HsmsSType sType)
        {
            switch (sType)
            {
                case HsmsSType.SelectReq:
                case HsmsSType.SelectRsp:
                    if (State == HsmsConnectionState.Selected)
                        throw new HsmsProtocolException($"{sType} received while already Selected.");
                    if (State == HsmsConnectionState.NotConnected)
                        throw new HsmsProtocolException($"{sType} received before TCP connection was established.");
                    break;

                case HsmsSType.DataMessage:
                    if (State != HsmsConnectionState.Selected)
                        throw new HsmsProtocolException($"Data Message received while not Selected (current state: {State}).");
                    break;

                case HsmsSType.LinktestReq:
                case HsmsSType.LinktestRsp:
                    if (State == HsmsConnectionState.NotConnected)
                        throw new HsmsProtocolException($"{sType} received before TCP connection was established.");
                    break;

                case HsmsSType.DeselectReq:
                case HsmsSType.DeselectRsp:
                case HsmsSType.SeparateReq:
                    if (State != HsmsConnectionState.Selected)
                        throw new HsmsProtocolException($"{sType} received while not Selected (current state: {State}).");
                    break;

                case HsmsSType.RejectReq:
                    break; // Reject.req is legal in any connected state by definition.
            }
        }

        private void Transition(HsmsConnectionState from, HsmsConnectionState to)
        {
            if(State != from)
                throw new HsmsProtocolException($"Invalid transition {from}->{to}: current state is {State}.");
            State = to;
            StateChanged?.Invoke(from, to);
        }
    }
}
