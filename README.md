# Unreliable-Loss-Detector

## Features
- Detector for lost unreliable packets

## Overview
The project provides helper class for detection of lost unreliable packets. This is important part of many advanced synchronization strategies. For example, Eventual Consistency uses it.

For simplicity, the server supports only one client.

| Overview  |  
|--|
| [![][preview1]](https://www.youtube.com/watch?v=tZppqKbpuL4) |

## Detector

Works by blah blah blah

`ILossHandler.OnPacketLost` will be called for each lost packet AND for each packet which was enqueued and un-acked when that peer got disconnected.

### Methods

- *LossDetector*(ILossHandler handler) - That implementation of `ILossHandler` will be called for each lost packet.
- *AddPeer*(Peer peer) - You should pass implementation of `ILossHandler` into constructor. 
- *RemovePeer*(Peer peer) - You should pass implementation of `ILossHandler` into constructor. 
- *AddHeaderForPeerId*(ushort peerId, BitBuffer data) - You should pass implementation of `ILossHandler` into constructor. 
- *ReadHeaderOfPeerId*(ushort peerId, BitBuffer data) - You should pass implementation of `ILossHandler` into constructor. 
- *ClearHeader*(BitBuffer data) - You should pass implementation of `ILossHandler` into constructor. 
- *GetDebugString*(StringBuilder builder) - You should pass implementation of `ILossHandler` into constructor. 
- *GetDebugString*(ushort peerId, StringBuilder builder) - You should pass implementation of `ILossHandler` into constructor. 

## Config
Common configuration class for all parts of the framework. Can be easily replaced by your config class.

### Fields
- MaxClientCount - How many clients can be connected to the server at the same time. That field is used for initialization of internal buffers.
- AckWindow 	 - How many un-acked packets there can be at the same time. That field is used for initialization of internal buffers.

## Dependencies
- Unity 2019.1
- ENet CSharp 2.2.6 

[preview1]: https://i.imgur.com/tzS55KM.png
