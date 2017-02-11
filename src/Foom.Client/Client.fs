﻿[<RequireQualifiedAccess>]
module Foom.Client.Client

open Foom.Ecs
open Foom.Renderer

type IsSky = IsSky of bool

let init (world: World) =
    let app = Backend.init ()
    let renderSystem = 
        app
        |> RendererSystem.create
            [
                ("Sky",
                    (fun _ _ _ -> null),
                    (fun shaderProgram ->
                        fun o run -> 
                            run RenderPass.Stencil2)
                )
                ("Sprite",
                    (fun em ent renderer ->
                        match em.TryGet<RendererSystem.SpriteComponent> (ent) with
                        | Some spriteComp -> (em, spriteComp) :> obj
                        | _ -> null
                    ),
                    (fun shaderProgram ->

                        let in_center = shaderProgram.CreateVertexAttributeVector3 ("in_center")
                        let in_texture = shaderProgram.CreateUniformRenderTexture ("uni_texture")

                        fun o run ->
                            match o with
                            | :? (EntityManager * RendererSystem.SpriteComponent) as o ->
                                let (_, spriteComp) = o
                                in_center.Set spriteComp.Center
                                ()
                                //run RenderPass.Stencil1
                            | _ -> ()

                    )
                )
                ("TextureMesh",
                    (fun em ent renderer ->
                        match em.TryGet<RendererSystem.MeshRenderComponent> (ent) with
                        | Some c -> 
                            let isSky = c.RenderInfo.MaterialInfo.TextureInfo.TexturePath.ToUpper().Contains("F_SKY1")
                            printfn "%s" c.RenderInfo.MaterialInfo.TextureInfo.TexturePath
                            if isSky then
                                IsSky (true) :> obj
                            else
                                null
                        | _ -> null
                    ),
                    (fun shaderProgram ->

                        fun o run ->
                            match o with
                            | :? IsSky as o -> 
                                let (IsSky (isSky)) = o
                                if isSky then
                                    ()
                                    run RenderPass.Stencil1
                                else
                                    run RenderPass.Depth
                            | _ -> run RenderPass.Depth

                    )
                )
            ]

    let renderSystemUpdate = world.AddBehavior (Behavior.merge [ renderSystem ])
    let inputUpdate = world.AddBehavior (Player.preUpdate app)

    let clientSubworld = world.CreateSubworld ()
    let clientWorld = ClientWorld.Create (clientSubworld, world.SpawnEntity ())
    let clientSystemUpdate = ClientSystem.create app clientWorld |> clientSubworld.AddBehavior

    world.Publish (ClientSystem.LoadWadAndLevelRequested ("doom.wad", "e1m1"))
   // world.Publish (ClientSystem.LoadWadAndLevelRequested ("doom2.wad", "map10"))

    {
        Window = app.Window
        AlwaysUpdate = fun () -> inputUpdate ()
        Update = clientSystemUpdate
        RenderUpdate = renderSystemUpdate
        ClientWorld = clientWorld
    }

let draw currentTime t (prev: ClientState) (curr: ClientState) =
    curr.RenderUpdate (currentTime, t)