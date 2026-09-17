using System;using System.IO;using System.Reflection;using UnityEditor;using UnityEngine;
namespace LightingBlocks.Editor {
[InitializeOnLoad] public static class RevisionActions {
static RevisionActions(){EditorApplication.update+=Poll;}
static void Poll(){const string request="Library/ForestRevision.request";if(EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(request))return;var parts=File.ReadAllText(request).Trim().Split('|');File.Delete(request);try {var type=typeof(RevisionActions).Assembly.GetType("LightingBlocks.Editor."+parts[0],true);type.GetMethod(parts[1],BindingFlags.Public|BindingFlags.Static).Invoke(null,null);File.WriteAllText("Library/ForestRevision.result","SUCCESS "+string.Join("|",parts));}catch(Exception e){File.WriteAllText("Library/ForestRevision.result",e.ToString());Debug.LogException(e);}}
}}
