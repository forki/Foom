﻿namespace Foom.Renderer

open System
open System.Drawing
open System.Numerics
open System.Collections.Generic

// *****************************************
// *****************************************
// Array Buffers
// *****************************************
// *****************************************

type Vector2Buffer (data) =

    let mutable id = 0
    let mutable length = 0
    let mutable queuedData = Some data

    member this.Set (data: Vector2 []) =
        queuedData <- Some data

    member this.Length = length

    member this.Bind () =
        if id <> 0 then
            Backend.bindVbo id

    member this.TryBufferData () =
        match queuedData with
        | Some data ->
            if id = 0 then
                id <- Backend.makeVbo ()
            
            Backend.bufferVbo data (sizeof<Vector2> * data.Length) id
            length <- data.Length
            queuedData <- None
            true
        | _ -> false

    member this.Id = id

type Vector3Buffer (data) =

    let mutable id = 0
    let mutable length = 0
    let mutable queuedData = Some data

    member this.Set (data: Vector3 []) =
        queuedData <- Some data

    member this.Length = length

    member this.Bind () =
        if id <> 0 then
            Backend.bindVbo id

    member this.TryBufferData () =
        match queuedData with
        | Some data ->
            if id = 0 then
                id <- Backend.makeVbo ()
            
            Backend.bufferVboVector3 data (sizeof<Vector3> * data.Length) id
            length <- data.Length
            queuedData <- None
            true
        | _ -> false

    member this.Id = id

type Vector4Buffer (data) =

    let mutable id = 0
    let mutable length = 0
    let mutable queuedData = Some data

    member this.Set (data: Vector4 []) =
        queuedData <- Some data

    member this.Length = length

    member this.Bind () =
        if id <> 0 then
            Backend.bindVbo id

    member this.TryBufferData () =
        match queuedData with
        | Some data ->
            if id = 0 then
                id <- Backend.makeVbo ()
            
            Backend.bufferVboVector4 data (sizeof<Vector4> * data.Length) id
            length <- data.Length
            queuedData <- None
            true
        | _ -> false

    member this.Id = id

type Matrix4x4Buffer (data) =

    let mutable id = 0
    let mutable length = 0
    let mutable queuedData = Some data

    member this.Set (data: Matrix4x4 []) =
        queuedData <- Some data

    member this.Length = length

    member this.Bind () =
        if id <> 0 then
            Backend.bindVbo id

    member this.TryBufferData () =
        match queuedData with
        | Some data ->
            if id = 0 then
                id <- Backend.makeVbo ()
            
            Backend.bufferVboMatrix4x4 data (sizeof<Matrix4x4> * data.Length) id
            length <- data.Length
            queuedData <- None
            true
        | _ -> false

    member this.Id = id

type Texture2DBuffer (bmp: Bitmap) =

    let mutable id = 0
    let mutable width = bmp.Width
    let mutable height = bmp.Height
    let mutable queuedData = Some bmp
    let mutable isTransparent = false

    member this.Id = id

    member this.Width = width

    member this.Height = height

    member this.IsTransparent = isTransparent

    member this.Bind () =
        if id <> 0 then
            Backend.bindTexture id // this does activetexture0, change this eventually

    member this.TryBufferData () =
        match queuedData with
        | Some bmp ->
            isTransparent <- bmp.PixelFormat = System.Drawing.Imaging.PixelFormat.Format32bppArgb

            let bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb)

            id <- Backend.createTexture bmp.Width bmp.Height bmpData.Scan0

            bmp.UnlockBits (bmpData)
            bmp.Dispose ()
            queuedData <- None
            true
        | _ -> false