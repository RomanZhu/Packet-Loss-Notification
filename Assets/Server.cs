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

public class Server : MonoBehaviour, ILossHandler
{
    public string Ip = "localhost";

    private readonly RingBuffer<Event>    _eventsToHandle = new RingBuffer<Event>(1024);
    private readonly RingBuffer<SendData> _sendData       = new RingBuffer<SendData>(1024);
    private readonly Host                 _host           = new Host();

    private Peer   _client;
    private Thread _networkThread;

    private ushort _tick;
    private ushort _undelivered;
    private ushort _receivedUnreliable;
    private ushort _sentUnreliable;

    private readonly BitBuffer     _buffer = new BitBuffer();
    private          LossDetector  _detector;
    private          StringBuilder _builder;

    void Start()
    {
        _builder  = new StringBuilder(1024, 1024);
        _detector = new LossDetector(this);

        var address = new Address();
        address.SetHost(Ip);
        address.Port = 9500;
        _host.Create(address, 1);

        _networkThread = NetworkThread();
        _networkThread.Start();
    }

    private void OnDestroy()
    {
        if (_client.IsSet)
        {
            _client.DisconnectNow(0);
            _host.Flush();
        }

        Thread.Sleep(20);
        _networkThread?.Abort();
    }

    private void OnGUI()
    {
        GUILayout.Label($"Tick {_tick}");
        GUILayout.Label($"Undelivered {_undelivered}");
        GUILayout.Label($"SentUnreliable {_sentUnreliable}");
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
                while (_sendData.TryDequeue(out var data))
                {
                    _host.Flush();
                    data.Peer.Send(0, ref data.Packet);
                }


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
                case EventType.Timeout:
                    OnDisconnected(@event.Peer);
                    break;
                case EventType.Receive:
                    unsafe
                    {
                        _receivedUnreliable++;
                        var packet = @event.Packet;
                        var span   = new ReadOnlySpan<byte>(packet.Data.ToPointer(), packet.Length);
                        _buffer.FromSpan(ref span, packet.Length);
                        _detector.ReadHeaderOfPeerId((ushort) @event.Peer.ID, _buffer);
                        _buffer.Clear();
                        packet.Dispose();
                        break;
                    }
            }
        }
    }

    void FixedUpdate()
    {
        if (_client.IsSet)
        {
            _tick++;
            SendUnreliable(_tick);
        }
    }

    private void SendUnreliable(uint value)
    {
        unsafe
        {
            _sentUnreliable++;
            var id = _detector.AddHeaderForPeerId((ushort) _client.ID, _buffer);

            _buffer.AddUInt(value);

            var length = _buffer.Length;
            var ptr    = Marshal.AllocHGlobal(length);
            var span   = new Span<byte>(ptr.ToPointer(), length);
            _buffer.ToSpan(ref span);

            var data = new PacketData
            {
                Data = ptr, Length = length, SequenceId = id
            };

            _buffer.Clear();

            var canSend = _detector.EnqueueData(_client, data);

            if (canSend)
            {
                var packet = new Packet();
                packet.Create(data.Data, data.Length, PacketFlags.None);
                _sendData.Enqueue(new SendData {Packet = packet, Peer = _client});
            }
            else
                _client.DisconnectNow(0);
        }
    }

    private void OnDisconnected(Peer eventPeer)
    {
        _client = new Peer();
        _detector.RemovePeer(eventPeer);
    }

    private void OnConnected(Peer eventPeer)
    {
        _client = eventPeer;
        _detector.AddPeer(eventPeer);
    }

    public void OnPacketLost(ushort peerId, PacketData data)
    {
        unsafe
        {
            if(!_client.IsSet)
                return;
            
            _undelivered++;
            var span = new ReadOnlySpan<byte>(data.Data.ToPointer(), data.Length);
            _buffer.FromSpan(ref span, data.Length);

            _detector.ClearHeader(_buffer);
            var val = _buffer.ReadUInt();
            _buffer.Clear();

            SendUnreliable(val);
        }
    }
}