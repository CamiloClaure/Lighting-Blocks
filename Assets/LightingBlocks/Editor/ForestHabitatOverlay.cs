using UnityEditor;
using UnityEngine;
namespace LightingBlocks.Editor
{
    [CustomEditor(typeof(ForestWorld))]public sealed class ForestHabitatOverlay:UnityEditor.Editor
    {
        bool show=true;
        public override void OnInspectorGUI(){DrawDefaultInspector();show=EditorGUILayout.Toggle("Show forest habitat overlay",show);EditorGUILayout.HelpBox("Cyan: hauling paths. Gold: output clearings. Green: trunks. Violet: wildlife habitats. Layout is shared with the player build.",MessageType.Info);if(GUILayout.Button("Rebuild using authored configuration"))TwilightBuilder.Apply();}
        void OnSceneGUI(){if(!show)return;var world=(ForestWorld)target;if(world.configuration==null)return;
            foreach(var d in world.configuration.islands){var layout=world.Layouts.Find(l=>l.definition.id==d.id);if(layout==null)layout=ForestLayoutGenerator.Generate(d,1907,Landscape.Shores[d.polygonIndex],world.configuration.trees.Length);
                Handles.color=new Color(.2f,.8f,1,.5f);float centre=d.wisdomPosition.x+12;var poly=Landscape.Shores[d.polygonIndex];float low=float.MaxValue,high=float.MinValue;foreach(var p in poly){low=Mathf.Min(low,p.y);high=Mathf.Max(high,p.y);}for(int side=-1;side<=1;side+=2)Handles.DrawLine(new Vector3(centre+side*3,.1f,low),new Vector3(centre+side*3,.1f,high));
                Handles.DrawLine(d.wisdomPosition+Vector3.right*2,new Vector3(centre,0,d.wisdomPosition.z));Handles.color=Color.yellow;foreach(var offset in d.outputOffsets)Handles.DrawWireCube(d.wisdomPosition+offset,new Vector3(1,.5f,1));
                Handles.color=Color.green;foreach(var tree in layout.trees)Handles.DrawWireDisc(tree.position+Vector3.up*.1f,Vector3.up,.7f);Handles.color=new Color(.7f,.4f,1);foreach(var p in layout.habitats)Handles.DrawWireDisc(p+Vector3.up*.1f,Vector3.up,1.5f);
                int interior=0;foreach(var t in layout.trees)if(Mathf.Abs(t.position.x-centre)<17)interior++;Handles.Label(d.wisdomPosition+Vector3.up*8,d.id+" · "+layout.trees.Count+" trees / "+interior+" interior · "+layout.cover.Count+" plants");
            }
        }
    }
}
