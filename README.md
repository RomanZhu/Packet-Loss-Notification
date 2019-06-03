

# Packet Loss Notification

## Features
- Notification about each lost unreliable packet
- Transport-agnostic
- Simple logger

## Overview
The project provides a helper class for detection and notification about lost unreliable packets. This is an important part of many advanced synchronization strategies. For example, Eventual Consistency uses it.

For simplicity, the server supports only one client.

| Overview  |  
|--|
| [![][preview1]](https://www.youtube.com/watch?v=bYH0_b6mEjY) |

## Detector

Detection works by adding that header to each sent packet:
- `ushort` Local Sequence Id
- `ushort` Last Remote Sequence Id
- `uint` Bitmask for a 32  previous remote sequence Ids

The workflow is like that:
- Peer Connected:
- -  Call `AddPeer` in helper
- Sending packet to Peer:
- - Get empty BitBuffer
- - Call `AddHeaderForPeerId`
- - Write actual payload to BitBuffer
- - Copy BitBuffer's content to allocated memory 
- - Clear BitBuffer
- - Call `EnqueueData`
- - - If result is TRUE
- - - - Send packet to peer
- - - If result is FALSE
- - - - Disconnect that peer
- Received packet from Peer:
- - Get packet's content into BitBuffer
- - Call `ReadHeaderOfPeerId` with it
- - - If result is TRUE
- - - - Read actual payload
- Periodically:
- - Call `ExecuteLostPackets`
- On lost packet:
- - Get data into BitBuffer
- - Call `ClearHeader`
- - Read actual payload
- Peer Disconnected:
- - Call `RemovePeer`

If packet is considered as *Lost* it means that it was *most likely* lost. When packet is considered as *Arrived*, then it is arrived for sure.

`ILossHandler.OnPacketLost` will be called for each lost packet AND for each packet which was enqueued and still tracked when that peer got disconnected.

If `LOSS_DETECTOR_DEBUG` symbol is present, then all lost packet sequences will be outputted to the logger.

### Methods

- void *LossDetector*(`ILossHandler handler, ushort maxPeerCount, ushort ackWindow`) - That implementation of `ILossHandler` will be called for each lost packet. `MaxPeerCount` - Max count of peers connected to that peer. `AckWindow` - Max count of local tracked packets per peer.
- void *AddPeer*(`ushort peerId`) - Clears sequence number for that peerId.
- void *RemovePeer*(`ushort peerId`) - Enqueues each tracked packet for that peerId as being lost. 
- void *AddHeaderForPeerId*(`ushort peerId, BitBuffer data`) - Writes header information into BitBuffer for that peerId.
- bool *ReadHeaderOfPeerId*(`ushort peerId, BitBuffer data`) - Reads header from BitBuffer and detects if any tracked packet is ACKed or NACKed. In the case of ACK detector will free allocated memory. Returns True if sequenceId is greater than lastSequenceId for that peerId.
- bool *EnqueueData*(`ushort peerId, PacketData data`) - Tries to put data into tracked packet queue for that peer. Returns False if the queue is already full. Probably you have not received packets from another peer for too long and should disconnect him.
- void *ExecuteLostPackets*() - Forwards all buffered lost packets to the handler and free their allocated memory. 
- void *ClearHeader*(`BitBuffer data`) - Clears header from BitBuffer.

If `LOSS_DETECTOR_DEBUG` symbol is present, then those methods are available too:
- void *GetDebugString*(`StringBuilder builder`) - Writes full state of buffers for each peerId into StringBuilder.
- void *GetDebugString*(`ushort peerId, StringBuilder builder`) - Writes state of buffers for that peerId.

## Dependencies
- Unity 2019.1
- NetStack.Serialization

*Helper itself is transport-agnostic, but example project uses ENet*
- ENet CSharp 2.2.6 

## Credits
[fholm](https://github.com/fholm) - The Master of Networking - has helped a lot.

[preview1]: https://i.imgur.com/7wiz68P.png
