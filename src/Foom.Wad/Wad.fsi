﻿namespace Foom.Wad

open System
open System.IO

open Foom.Wad.Pickler
open Foom.Wad.Level

type FlatTexture =
    {
        Pixels: Pixel []
        Name: string
    }

[<Sealed>]
type Wad

[<CompilationRepresentationAttribute (CompilationRepresentationFlags.ModuleSuffix)>]
module Wad =

    val create : Stream -> Async<Wad>

    val createFromWad : Wad -> Stream -> Async<Wad>

    val flats : Wad -> FlatTexture []

    val findLevel : levelName: string -> wad: Wad -> Async<Level>

    val tryLoadGraphic : string -> Wad -> (DoomPicture * string) option

    val loadPatches : Wad -> (DoomPicture * string) []