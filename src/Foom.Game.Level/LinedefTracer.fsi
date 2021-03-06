﻿namespace Foom.Game.Level

open System.Numerics

open Foom.Geometry

type LinedefPolygon = 
    {
        Linedefs: Wall list
        Inner: LinedefPolygon list
    }

[<CompilationRepresentationAttribute (CompilationRepresentationFlags.ModuleSuffix)>]
module LinedefTracer =

    val run : sectorId: int -> Level -> LinedefPolygon list

module Polygon =

    val ofLinedefs : Wall list -> int -> Polygon2D