using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using RingBuffer.Concurrent;
using ENet;
using LossDetection;
using NetStack.Serialization;
using UnityEngine;
using Event = ENet.Event;
using EventType = ENet.EventType;

public class Client : MonoBehaviour, ILossHandler
{
    public string Ip = "localhost";

    private readonly RingBuffer<Event>    _eventsToHandle = new RingBuffer<Event>(1024);
    private readonly RingBuffer<SendData> _sendData       = new RingBuffer<SendData>(1024);
    private readonly Host                 _host           = new Host();

    public  Peer   ServerConnection;
    private Thread _networkThread;

    private readonly BitBuffer    _buffer = new BitBuffer();
    private          LossDetector _detector;

    private StringBuilder _builder;

    void Start()
    {
        _builder  = new StringBuilder(1024, 1024);
        _detector = new LossDetector(this, 1, 256);

        _host.Create();
        var address = new Address();
        address.SetHost(Ip);
        address.Port = 9500;
        _host.Connect(address, 1);

        _networkThread = NetworkThread();
        _networkThread.Start();
    }

    private void OnDestroy()
    {
        if (ServerConnection.IsSet)
        {
            ServerConnection.DisconnectNow(0);
            _host.Flush();
        }

        Thread.Sleep(20);
        _networkThread?.Abort();
    }

    private ushort _tick;
    private ushort _undelivered;
    private ushort _sent;
    private ushort _receivedUnreliable;

    private void OnGUI()
    {
        GUILayout.Label($"Tick {_tick}");
        GUILayout.Label($"Undelivered {_undelivered}");
        GUILayout.Label($"SentUnreliable {_sent}");
        GUILayout.Label($"ReceivedUnreliable {_receivedUnreliable}");

        _builder.Clear();
        _detector.GetDebugString(_builder);

        GUILayout.Label(_builder.ToString());
    }

    private Thread NetworkThread()
    {
        return new Thread(() =>
        {
            while (true)
            {
                while (_sendData.TryDequeue(out var data)) data.Peer.Send(0, ref data.Packet);


                if (!_host.IsSet) Thread.Sleep(15);
                if (_host.Service(15, out var @event) > 0) _eventsToHandle.Enqueue(@event);
            }
        });
    }


    private void Update()
    {
        while (_eventsToHandle.TryDequeue(out var @event))
        {
            switch (@event.Type)
            {
                case EventType.Connect:
                    OnConnected(@event.Peer);
                    break;
                case EventType.Disconnect:
                    OnDisconnected(@event.Peer);
                    break;
                case EventType.Receive:
                    unsafe
                    {
                        var packet = @event.Packet;
                        var span   = new ReadOnlySpan<byte>(packet.Data.ToPointer(), packet.Length);
                        _buffer.FromSpan(ref span, packet.Length);
                        var valid = _detector.ReadHeaderOfPeerId((ushort) @event.Peer.ID, _buffer);

                        if (valid)
                        {
                            var val = _buffer.ReadUInt();
                            _tick = (ushort) val;
                        }

                        _receivedUnreliable++;
                        _buffer.Clear();
                        packet.Dispose();
                        break;
                    }

                case EventType.Timeout:
                    OnDisconnected(@event.Peer);
                    break;
            }
        }
        
        _detector.ExecuteLostPackets();
    }

    private void FixedUpdate()
    {
        if (ServerConnection.IsSet)
        {
            unsafe
            {
                var id     = _detector.AddHeaderForPeerId((ushort) ServerConnection.ID, _buffer);
                var length = _buffer.Length;
                var ptr    = Marshal.AllocHGlobal(length);
                var span   = new Span<byte>(ptr.ToPointer(), length);
                
                _buffer.ToSpan(ref span);

                var data = new PacketData
                {
                    Data = ptr, Length = length, SequenceId = id
                };

                _buffer.Clear();

                var canSend = _detector.EnqueueData((ushort)ServerConnection.ID, data);

                if (canSend)
                {
                    _sent++;
                    var packet = new Packet();
                    packet.Create(data.Data, data.Length, PacketFlags.None);
                    _sendData.Enqueue(new SendData {Packet = packet, Peer = ServerConnection});
                }
                else
                    ServerConnection.DisconnectNow(0);
            }
        }
    }

    private void OnDisconnected(Peer eventPeer)
    {
        _detector.RemovePeer((ushort)eventPeer.ID);
        ServerConnection = new Peer();
    }

    private void OnConnected(Peer eventPeer)
    {
        _detector.AddPeer((ushort)eventPeer.ID);
        ServerConnection = eventPeer;
    }

    public void OnPacketLost(ushort peerId, PacketData data)
    {
        _undelivered++;
    }
}

public struct SendData
{
    public Peer   Peer;
    public Packet Packet;
}