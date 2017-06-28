﻿namespace Foom.Network

open System
open System.Collections.Generic

type PeerFlow (f) =

    let packetPool = PacketPool 1024

    let receiverUnreliable =
        Receiver.createUnreliable packetPool f

    let receiverReliableOrdered =
        Receiver.createReliableOrdered packetPool (fun _ -> ()) f

    let senderUnreliable = 
        Sender.createUnreliable packetPool f

    let senderReliableOrdered =
        Sender.createReliableOrdered packetPool f

    

[<Sealed>]
type ConnectedClient (endPoint: IUdpEndPoint, udpServer: IUdpServer) as this =

    let packetPool = PacketPool 1024

    let packetQueue = Queue<Packet> ()

    // Pipelines

    // Senders
    let senderUnreliable = 
        Sender.createUnreliable packetPool (fun packet -> 
            this.SendNow (packet.Raw, packet.Length)
        )

    let senderReliableOrdered =
        Sender.createReliableOrdered packetPool (fun packet ->
            this.SendNow (packet.Raw, packet.Length)
        )

    member this.SendNow (data : byte [], size) =
        if size > 0 && data.Length > 0 then
            udpServer.Send (data, size, endPoint) |> ignore

    member this.Send (data, startIndex, size) =
        senderUnreliable.Send { bytes = data; startIndex = startIndex; size = size }

    member this.SendConnectionAccepted () =
        let packet = Packet ()
        packet.Type <- PacketType.ConnectionAccepted

        packetQueue.Enqueue packet

    member this.Update time =

        while packetQueue.Count > 0 do
            let packet = packetQueue.Dequeue ()
            this.SendNow (packet.Raw, packet.Length)

        senderUnreliable.Process time
