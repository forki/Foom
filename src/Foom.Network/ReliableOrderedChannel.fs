﻿namespace Foom.Network

open System
open System.Collections.Generic

[<AutoOpen>]
module ReliableOrderedChannelImpl =

//http://gafferongames.com/networking-for-game-programmers/reliability-and-flow-control/
//bool sequence_more_recent( unsigned int s1, 
//                           unsigned int s2, 
//                           unsigned int max )
//{
//    return 
//        ( s1 > s2 ) && 
//        ( s1 - s2 <= max/2 ) 
//           ||
//        ( s2 > s1 ) && 
//        ( s2 - s1  > max/2 );
//}
    let sequenceMoreRecent (s1 : uint16) (s2 : uint16) =
        (s1 > s2) &&
        (s1 - s2 <= UInt16.MaxValue / 2us)
            ||
        (s2 > s1) &&
        (s2 - s1 > UInt16.MaxValue / 2us)

type ReliableOrderedChannel (packetPool : PacketPool) =

    let mutable nextId = 0us
    let mutable oldestId = -1
    let mutable newestId = -1
    let mutable oldestTimeAck = DateTime ()

    let copyPacketPool = PacketPool (64)
    let acks = Array.init 65536 (fun _ -> true)
    let ackTimes = Array.init 65536 (fun _ -> DateTime ())
    let packets = Array.init 65536 (fun _ -> Unchecked.defaultof<Packet>)

    member this.HasPendingAcks = oldestId >= 0 || newestId >= 0

    member this.SendData (data, startIndex, size, f) =
        let packet = packetPool.Get ()
        packet.SetData (PacketType.ReliableOrdered, data, startIndex, size)
        packet.SequenceId <- nextId

        let copypacket = copyPacketPool.Get ()
        copypacket.SetData (PacketType.ReliableOrdered, data, startIndex, size)
        copypacket.SequenceId <- nextId

        let id = int nextId
        if not acks.[id] then
            failwith "This should never happened. We waiting too long."

        let dt = DateTime.UtcNow

        acks.[id] <- false
        ackTimes.[id] <- dt
        packets.[id] <- copypacket

        if oldestId = -1 then
            oldestId <- id
            oldestTimeAck <- dt

        if newestId = -1 then
            newestId <- id
        elif sequenceMoreRecent (uint16 id) (uint16 newestId) then
            newestId <- id

        nextId <- nextId + 1us
        f packet

    member this.TryGetNonAckedPacket (id : uint16, f) =
        let dt = DateTime.UtcNow

        if newestId = -1 && oldestId = -1 then
            ()
        else
            if not acks.[int id] then
                let copypacket = packets.[int id]
                let packet = packetPool.Get ()

                Buffer.BlockCopy (copypacket.Raw, 0, packet.Raw, 0, copypacket.Length)
                packet.Length <- copypacket.Length
                f packet


    member this.Ack (id : uint16) =
        let i = int id
        acks.[i] <- true
        ackTimes.[i] <- DateTime ()
        copyPacketPool.Recycle packets.[i]
        packets.[i] <- Unchecked.defaultof<Packet>

        if oldestId = i && newestId = i then
            oldestId <- -1
            oldestTimeAck <- DateTime ()
            newestId <- -1
        elif oldestId = i then
            let mutable nextOldestId = -1
            if newestId > oldestId then
                for j = newestId downto oldestId do
                    if not acks.[j] then
                        nextOldestId <- j
                    
            elif newestId < oldestId then
                for j = newestId downto 0 do
                    if not acks.[j] then
                        nextOldestId <- j

                for j = 65536 - 1 downto oldestId do
                    if not acks.[j] then
                        nextOldestId <- j

            if nextOldestId = -1 then
                oldestId <- -1
                oldestTimeAck <- DateTime ()
            else
                oldestId <- nextOldestId
                oldestTimeAck <- ackTimes.[oldestId]

        elif newestId = i then
            let mutable nextNewestId = -1
            if newestId > oldestId then
                for j = oldestId to newestId do
                    if not acks.[j] then
                        nextNewestId <- j
                    
            elif newestId < oldestId then
                for j = oldestId to 65536 - 1 do
                    if not acks.[j] then
                        nextNewestId <- j

                for j = 0 to newestId do
                    if not acks.[j] then
                        nextNewestId <- j

            newestId <- nextNewestId
                  
    member this.TryResend resend =
        let dt = DateTime.UtcNow

        if newestId > oldestId then
            for j = newestId downto oldestId do
                this.TryGetNonAckedPacket (uint16 j, resend)
                
        elif newestId < oldestId then
            for j = newestId downto 0 do
                if not acks.[j] then
                    this.TryGetNonAckedPacket (uint16 j, resend)

            for j = 65536 - 1 downto oldestId do
                if not acks.[j] then
                    this.TryGetNonAckedPacket (uint16 j, resend)
        else
            this.TryGetNonAckedPacket (uint16 oldestId, resend)


type ReliableOrderedChannelReceiver (packetPool : PacketPool) =

    let mutable nextId = 0us
    let mutable newestId = 0us

    let copyPacketPool = PacketPool (64)
    let packets = Array.init 65536 (fun _ -> Unchecked.defaultof<Packet>)

    let rec tryProcess f = function
        | i ->
            if newestId > nextId then
                let packet = packets.[i]
                if obj.ReferenceEquals (packet, null) |> not then
                    packets.[i] <- Unchecked.defaultof<Packet>
                    f packet
                    tryProcess f (i + 1)

    member this.SendData (sequenceId, data, startIndex, size, f) =
        let packet = packetPool.Get ()
        packet.SetData (PacketType.ReliableOrdered, data, startIndex, size)
        packet.SequenceId <- sequenceId

        if sequenceMoreRecent nextId sequenceId then
            failwith "This should not happen."

        if nextId = sequenceId then
            nextId <- nextId + 1us
            if sequenceMoreRecent nextId newestId then
                newestId <- nextId
            f packet
        else
            packetPool.Recycle packet

            let copypacket = copyPacketPool.Get ()
            copypacket.SetData (PacketType.ReliableOrdered, data, startIndex, size)
            copypacket.SequenceId <- sequenceId

            if sequenceMoreRecent sequenceId newestId then
                newestId <- sequenceId

            packets.[int sequenceId] <- copypacket

    member this.SendPacket (packet : Packet, f) =
        this.SendData (packet.SequenceId, packet.Raw, sizeof<PacketHeader>, packet.Length - sizeof<PacketHeader>, f)

    member x.TryProcess f =
        let mutable done' = false

        if newestId > nextId then
            for i = int nextId to int newestId do
                let packet = packets.[i]
                if obj.ReferenceEquals (packet, null) |> not && not done' then
                    nextId <- nextId + 1us
                    f packet
                else
                    done' <- true
                  
        elif newestId < nextId then
            for i = int nextId to 65536 - 1 do
                let packet = packets.[i]
                if obj.ReferenceEquals (packet, null) |> not && not done' then
                    nextId <- nextId + 1us
                    f packet
                else
                    done' <- true

            for i = 0 to int newestId do
                let packet = packets.[i]
                if obj.ReferenceEquals (packet, null) |> not && not done' then
                    nextId <- nextId + 1us
                    f packet
                else
                    done' <- true

        else
            let i = int nextId
            let packet = packets.[i]
            if obj.ReferenceEquals (packet, null) |> not && not done' then
                nextId <- nextId + 1us
                f packet
            else
                done' <- true

        if sequenceMoreRecent nextId newestId then
            newestId <- nextId
