﻿module internal Foom.Wad.Pickler

open System
open System.Numerics
open System.Collections.Generic
open Foom.Pickler.Core
open Foom.Pickler.Unpickle

type Header = { IsPwad: bool; LumpCount: int; LumpOffset: int }
 
type LumpHeader = { Offset: int32; Size: int32; Name: string }
 
type WadData = { Header: Header; LumpHeaders: LumpHeader [] }

type ThingFormat =
    | Doom = 0
    | Hexen = 1

type LumpThings = { Things: Thing [] }
type LumpLinedefs = { Linedefs: Dictionary<int, Linedef ResizeArray> }
type LumpSidedefs = { Sidedefs: Sidedef [] }
type LumpVertices = { Vertices: Vector2 [] }
type LumpSectors = { Sectors: Sector [] }

type PaletteData = { Pixels: Pixel [] }

type TextureHeader =
    {
        Count: int
        Offsets: int []
    }

type TexturePatch =
    {
        OriginX: int
        OriginY: int
        PatchNumber: int
    }

type TextureInfo =
    {
        Name: string
        IsMasked: bool
        Width: int
        Height: int
        Patches: TexturePatch []
    }

type DoomPicture =
    {
        Width: int
        Height: int
        Top: int
        Left: int
        Data: Pixel [,]
    }

module UnpickleWad =

    val u_lumpHeader : Unpickle<LumpHeader>

    val u_lumpHeaders : count: int -> offset: int64 -> Unpickle<LumpHeader []>

    val u_wad : Unpickle<WadData>

    val u_lumpThings : format: ThingFormat -> size: int -> offset: int64 -> Unpickle<LumpThings>

    val u_lumpVertices : size: int -> offset: int64 -> Unpickle<LumpVertices>

    val u_lumpSidedefs : size: int -> offset: int64 -> Unpickle<LumpSidedefs>

    val u_lumpLinedefs : vertices: Vector2 [] -> sidedefs: Sidedef [] -> size: int -> offset: int64 -> Unpickle<LumpLinedefs>

    val u_lumpSectors : linedefs: Dictionary<int, Linedef ResizeArray> -> size: int -> offset: int64 -> Unpickle<LumpSectors>

    val u_lumpPalettes : size: int -> offset: int64 -> Unpickle<PaletteData []>

    val u_lumpRaw : size: int -> offset: int64 -> Unpickle<byte []>

    val uTextureHeader : LumpHeader -> Unpickle<TextureHeader>

    val uTextureInfos : LumpHeader -> TextureHeader -> Unpickle<TextureInfo []>

    val uPatchNames : LumpHeader -> Unpickle<string []>

    val uDoomPicture : LumpHeader -> PaletteData -> Unpickle<DoomPicture>