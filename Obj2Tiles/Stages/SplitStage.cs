﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Obj2Tiles.Library.Geometry;

namespace Obj2Tiles.Stages;

public static partial class StagesFacade
{
    
    public static async Task<Dictionary<string, Box3>[]> Split(string[] sourceFiles, string destFolder, int divisions, bool zsplit, Box3 bounds)
    {
        var tasks = new List<Task<Dictionary<string, Box3>>>();

        for (var index = 0; index < sourceFiles.Length; index++)
        {
            var file = sourceFiles[index];

            // We compress textures except the first one (the original one)
            var splitTask = Split(file, Path.Combine(destFolder, "LOD-" + index),
                divisions, zsplit, bounds,
                index == 0 ? TexturesStrategy.Repack : TexturesStrategy.RepackCompressed);

            tasks.Add(splitTask);
        }

        await Task.WhenAll(tasks);
        
        return tasks.Select(task => task.Result).ToArray();
        
    }
    
    public static async Task<Dictionary<string, Box3>> Split(string sourcePath, string destPath, int divisions, bool zSplit = false,
        Box3? bounds = null,
        TexturesStrategy textureStrategy = TexturesStrategy.Repack, SplitPointStrategy splitPointStrategy = SplitPointStrategy.VertexBaricenter)
    {
        var sw = new Stopwatch();
        var tilesBounds = new Dictionary<string, Box3>();

        Console.WriteLine($" -> Loading OBJ file \"{sourcePath}\"");

        sw.Start();
        var mesh = MeshUtils.LoadMesh(sourcePath);

        Console.WriteLine($" ?> Loaded {mesh.VertexCount} vertices, {mesh.FacesCount} faces in {sw.ElapsedMilliseconds}ms");

        Console.WriteLine(
            $" -> Splitting with a depth of {divisions}{(zSplit ? " with z-split" : "")}");

        var meshes = new ConcurrentBag<IMesh>();

        sw.Restart();

        int count;
        
        if (bounds != null)
        {
            count = zSplit
                ? await MeshUtils.RecurseSplitXYZ(mesh, divisions, bounds, meshes)
                : await MeshUtils.RecurseSplitXY(mesh, divisions, bounds, meshes);
        }
        else
        {
            Func<IMesh, Vertex3> getSplitPoint = splitPointStrategy switch
            {
                SplitPointStrategy.AbsoluteCenter => m => m.Bounds.Center,
                SplitPointStrategy.VertexBaricenter => m => m.GetVertexBaricenter(),
                _ => throw new ArgumentOutOfRangeException(nameof(splitPointStrategy))
            };

            count = zSplit
                ? await MeshUtils.RecurseSplitXYZ(mesh, divisions, getSplitPoint, meshes)
                : await MeshUtils.RecurseSplitXY(mesh, divisions, getSplitPoint, meshes);
        }

        sw.Stop();

        Console.WriteLine(
            $" ?> Done {count} edge splits in {sw.ElapsedMilliseconds}ms ({(double)count / sw.ElapsedMilliseconds:F2} split/ms)");

        Console.WriteLine(" -> Writing tiles");

        Directory.CreateDirectory(destPath);

        sw.Restart();

        var ms = meshes.ToArray();
        for (var index = 0; index < ms.Length; index++)
        {
            var m = ms[index];

            if (m is MeshT t)
                t.TexturesStrategy = textureStrategy;

            m.WriteObj(Path.Combine(destPath, $"{m.Name}.obj"));

            tilesBounds.Add(m.Name, m.Bounds);
            
        }

        Console.WriteLine($" ?> {meshes.Count} tiles written in {sw.ElapsedMilliseconds}ms");
        
        return tilesBounds;
    }
}


public enum SplitPointStrategy
{
    AbsoluteCenter,
    VertexBaricenter
}