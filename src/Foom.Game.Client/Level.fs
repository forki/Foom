﻿[<RequireQualifiedAccess>] 
module Foom.Client.Level

open System
open System.IO
open System.Numerics
open System.Collections.Generic

open Foom.Ecs
open Foom.Math
open Foom.Physics
open Foom.Renderer
open Foom.Geometry
open Foom.Wad
open Foom.Client.Sky
open Foom.Renderer.RendererSystem

open Foom.Game.Core
open Foom.Game.Assets
open Foom.Game.Sprite
open Foom.Game.Level
open Foom.Game.Wad
open Foom.Game.Gameplay.Doom

let mutable globalAM = Unchecked.defaultof<AssetManager>

let loadTexture path =
    globalAM.AssetLoader.LoadTextureFile path
    //let tex = Texture (TextureKind.Single path)
    //globalAM.LoadTexture tex
    //tex

let globalBatch = Dictionary<string, Vector3 ResizeArray * Vector2 ResizeArray * Vector4 ResizeArray> ()

let levelMaterialCache = Dictionary<string, Texture> ()

let runGlobalBatch (em: EntityManager) =
    globalBatch
    |> Seq.iter (fun pair ->
        let isSky = pair.Key.Contains("F_SKY1")
        let texturePath = pair.Key
        let vertices, uv, color = pair.Value

        let ent = em.Spawn ()

        let pipelineName = if isSky then "Sky" else "World"
        let texture = 
            match levelMaterialCache.TryGetValue (texturePath) with
            | true, x -> x
            | _ ->
                let texture = Texture (TextureKind.Single texturePath)

                levelMaterialCache.[texturePath] <- texture

                texture

        let meshInfo : RendererSystem.MeshInfo =
            {
                Position = vertices |> Seq.toArray
                Uv = uv |> Seq.toArray
                Color = color |> Seq.toArray
            }

        em.Add (ent, RendererSystem.MeshRenderComponent (pipelineName, texture, meshInfo))
    )

open System.Linq

let spawnMesh sector (vertices: IEnumerable<Vector3>) uv (texturePath: string) =
    let lightLevel = sector.lightLevel
    let color = Array.init (vertices.Count ()) (fun _ -> Vector4 (single lightLevel, single lightLevel, single lightLevel, 255.f))

    match globalBatch.TryGetValue(texturePath) with
    | true, (gVertices, gUv, gColor) ->
        gVertices.AddRange(vertices)
        gUv.AddRange(uv)
        gColor.AddRange(color)
    | _ ->
        globalBatch.Add (texturePath, (ResizeArray vertices, ResizeArray uv, ResizeArray color))

let spawnSectorGeometryMesh sector (geo: SectorGeometry) wad =
    geo.TextureName
    |> Option.iter (fun textureName ->
        let texturePath = textureName + "_flat.bmp"
        let t = loadTexture texturePath//new Bitmap(texturePath)
        spawnMesh sector geo.Vertices (SectorGeometry.createUV t.Width t.Height geo) texturePath
    )

let spawnWallPartMesh sector (part: WallPart) (vertices: Vector3 []) wad isSky =
    if vertices.Length >= 3 then
        if not isSky then
            part.TextureName
            |> Option.iter (fun textureName ->
                let texturePath = textureName + ".bmp"
                let t = loadTexture texturePath//new Bitmap(texturePath)
                spawnMesh sector vertices (WallPart.createUV vertices t.Width t.Height part) texturePath
            )
        else
            let texturePath = "F_SKY1" + "_flat.bmp"
            let t = loadTexture texturePath//new Bitmap(texturePath)
            spawnMesh sector vertices (WallPart.createUV vertices t.Width t.Height part) texturePath

let spawnWallMesh level (wall: Wall) wad =
    let (
        (upperFront, middleFront, lowerFront),
        (upperBack, middleBack, lowerBack)) = Foom.Game.Level.Level.createWallGeometry wall level

    match wall.FrontSide with
    | Some frontSide ->

        let isSky =
            match wall.BackSide with
            | Some backSide ->
                let sector = Foom.Game.Level.Level.getSector backSide.SectorId level
                sector.ceilingTextureName.Equals("F_SKY1")
            | _ -> false
        
        let sector = Foom.Game.Level.Level.getSector frontSide.SectorId level

        spawnWallPartMesh sector frontSide.Upper upperFront wad isSky
        spawnWallPartMesh sector frontSide.Middle middleFront wad false
        spawnWallPartMesh sector frontSide.Lower lowerFront wad false

    | _ -> ()

    match wall.BackSide with
    | Some backSide ->

        let isSky =
            match wall.FrontSide with
            | Some frontSide ->
                let sector = Foom.Game.Level.Level.getSector frontSide.SectorId level
                sector.ceilingTextureName.Equals("F_SKY1")
            | _ -> false

        let sector = Foom.Game.Level.Level.getSector backSide.SectorId level

        spawnWallPartMesh sector backSide.Upper upperBack wad isSky
        spawnWallPartMesh sector backSide.Middle middleBack wad false
        spawnWallPartMesh sector backSide.Lower lowerBack wad false

    | _ -> ()

let updates openWad exportTextures am (clientWorld: ClientWorld) =
    globalAM <- am
    [
        Behavior.wadLoading
            openWad
            exportTextures
           // (fun name -> System.IO.File.Open (name, FileMode.Open) :> Stream)
            //(fun wad _ ->
            //    wad |> exportFlatTextures
            //    wad |> exportTextures
            //    wad |> exportSpriteTextures
            //)

        Behavior.levelLoading (fun wad level em ->
            let physicsEngineComp = PhysicsEngineComponent (128)

            let lvl = WadLevel.toLevel level

            let sectorCount = lvl |> Level.getSectorCount

            let sectorWalls =
                Array.init sectorCount (fun _ -> ResizeArray ())

            lvl
            |> Foom.Game.Level.Level.iteriSector (fun i sector ->

                (i, level)
                ||> Level.iterLinedefBySectorId (fun linedef ->
                    let isImpassible = (linedef.Flags.HasFlag(LinedefFlags.BlocksPlayersAndMonsters))
                    let isUpper = linedef.Flags.HasFlag (LinedefFlags.UpperTextureUnpegged)
                    let staticWall =
                        {
                            LineSegment = LineSegment2D (linedef.Start, linedef.End)

                            IsTrigger = (linedef.FrontSidedef.IsSome && linedef.BackSidedef.IsSome) //&& not isImpassible && isUpper

                        }

                    let rBody = RigidBody (StaticWall staticWall, Vector3.Zero)

                    physicsEngineComp.PhysicsEngine
                    |> PhysicsEngine.addRigidBody rBody

                    if isImpassible then
                        let staticWall =
                            {
                                LineSegment = LineSegment2D (linedef.End, linedef.Start)

                                IsTrigger = false

                            }

                        let rBody = RigidBody (StaticWall staticWall, Vector3.Zero)

                        physicsEngineComp.PhysicsEngine
                        |> PhysicsEngine.addRigidBody rBody                        
                )

                WadLevel.createSectorGeometry i lvl
                |> Seq.iter (fun (ceiling, floor) ->
                    spawnSectorGeometryMesh sector ceiling wad
                    spawnSectorGeometryMesh sector floor wad

                    let mutable j = 0
                    while j < floor.Vertices.Length do
                        let v0 = floor.Vertices.[j]
                        let v1 = floor.Vertices.[j + 1]
                        let v2 = floor.Vertices.[j + 2]

                        physicsEngineComp.PhysicsEngine
                        |> PhysicsEngine.addTriangle
                            (Triangle2D (
                                    Vector2 (v0.X, v0.Y),
                                    Vector2 (v1.X, v1.Y),
                                    Vector2 (v2.X, v2.Y)
                                )
                            )
                            sector // data to store for physics

                        j <- j + 3
                )
            )
            
            lvl
            |> Level.iterWall (fun wall ->
                spawnWallMesh lvl wall wad
            )

            level
            |> Foom.Wad.Level.iterThing (fun thing ->
                match thing with
                | Thing.Doom thing when thing.Flags.HasFlag (DoomThingFlags.SkillLevelFourAndFive) ->

                    let position = Vector3 (single thing.X, single thing.Y, 0.f)

                    match thing.Type with

                    | ThingType.ArmorBonus -> ArmorBonus.spawn position |> em.Spawn |> ignore 

                    | ThingType.GreenArmor -> GreenArmor.spawn position em |> ignore

                    | ThingType.BlueArmor -> BlueArmor.spawn position em |> ignore

                    | ThingType.SoulSphere -> SoulSphere.spawn position em |> ignore

                    | ThingType.BloodyMess
                    | ThingType.BloodyMess2 -> GibbedMarine.spawn position em |> ignore

                    | ThingType.ShotgunGuy -> ShotgunGuy.spawn position em |> ignore

                    | _ ->

                    let mutable image = None

                    match thing.Type with
                    | ThingType.HealthBonus -> image <- Some "BON1A0.bmp"
                    | ThingType.DeadPlayer -> image <- Some "PLAYN0.bmp"
                    | ThingType.Stimpack -> image <- Some "STIMA0.bmp"
                    | ThingType.Medkit -> image <- Some "MEDIA0.bmp"
                    | ThingType.Barrel -> image <- Some "BAR1A0.bmp"
                    | ThingType.TallTechnoPillar -> image <- Some "ELECA0.bmp"
                    | ThingType.Player1Start -> image <- Some "PLAYA1.bmp"
                    | ThingType.AmmoClip -> image <- Some "CLIPA0.bmp"
                    | _ -> ()

                    let pos = Vector2 (single thing.X, single thing.Y)
                    let sector = physicsEngineComp.PhysicsEngine |> PhysicsEngine.findWithPoint pos

                    match image with
                    | Some texturePath when sector <> null ->
                        let sector = sector :?> Foom.Game.Level.Sector
                        let pos = Vector3 (pos, single sector.floorHeight)

                        let pipelineName = "World"
                        let texture = 

                            match levelMaterialCache.TryGetValue (texturePath) with
                            | true, x -> x
                            | _ ->
                                let texture = Texture (TextureKind.Single texturePath)

                                levelMaterialCache.[texturePath] <- texture

                                texture

                        let ent = em.Spawn ()
                        em.Add (ent, TransformComponent (Matrix4x4.CreateTranslation(pos)))
                        em.Add (ent, SpriteComponent (pipelineName, texture, sector.lightLevel))
                    | _ -> ()

                | _ -> ()
            )

            runGlobalBatch em
            em.Add (clientWorld.Entity, physicsEngineComp)

            level
            |> Foom.Wad.Level.tryFindPlayer1Start
            |> Option.iter (function
                | Doom doomThing ->
                    let sector =
                        physicsEngineComp.PhysicsEngine
                        |> PhysicsEngine.findWithPoint (Vector2 (single doomThing.X, single doomThing.Y)) :?> Foom.Game.Level.Sector

                    let position = Vector3 (single doomThing.X, single doomThing.Y, single sector.floorHeight + 28.f)

                    let transformComp = TransformComponent (Matrix4x4.CreateTranslation (position))

                    let cameraEnt = em.Spawn ()
                    em.Add (cameraEnt, CameraComponent (Matrix4x4.CreatePerspectiveFieldOfView (56.25f * 0.0174533f, ((16.f + 16.f * 0.25f) / 9.f), 16.f, 100000.f)))
                    em.Add (cameraEnt, TransformComponent (Matrix4x4.CreateTranslation (position)))
                    em.Add (cameraEnt, CharacterControllerComponent (position, 15.f, 56.f))
                    em.Add (cameraEnt, PlayerComponent ())

                    let skyEnt = em.Spawn ()
                   // em.Add (skyEnt, CameraComponent (Matrix4x4.CreatePerspectiveFieldOfView (56.25f * 0.0174533f, ((16.f + 16.f * 0.25f) / 9.f), 16.f, 100000.f), LayerMask.Layer0, ClearFlags.None, 1))
                   // em.Add (skyEnt, TransformComponent (Matrix4x4.CreateTranslation (position)))

                    em.Add (skyEnt, SkyRendererComponent ("Sky", Texture (TextureKind.Single "milky2.jpg")))

                | _ -> ()
            )
        )
    ]