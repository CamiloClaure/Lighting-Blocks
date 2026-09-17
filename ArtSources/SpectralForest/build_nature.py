import bpy,math,json
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).parent; OUT=ROOT/'Exports';OUT.mkdir(parents=True,exist_ok=True)
report={}
def export(path):
 bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='STRIP',object_types={'MESH'},use_mesh_modifiers=True)
def lods(obj,name,targets):
 bpy.ops.object.select_all(action='DESELECT'); copies=[]
 for index,target in enumerate(targets):
  copy=obj.copy();copy.data=obj.data.copy();bpy.context.collection.objects.link(copy);copy.name=name+'_LOD'+str(index)
  bpy.context.view_layer.objects.active=copy;copy.select_set(True)
  tri=copy.modifiers.new('Triangulate','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
  if len(copy.data.polygons)>target:
   dec=copy.modifiers.new('Silhouette reduction','DECIMATE');dec.ratio=target/len(copy.data.polygons);bpy.ops.object.modifier_apply(modifier=dec.name)
  copies.append(copy);copy.select_set(False)
 for copy in copies:copy.select_set(True)
 export(OUT/(name+'.fbx'));report[name]=[len(c.data.polygons) for c in copies]
 for copy in copies:bpy.data.objects.remove(copy,do_unlink=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'AllModels.blend'))
for name in ['BirchTree_1','BirchTree_2','MapleTree_1','MapleTree_2','PineTree_1','PineTree_2','Bush','Grass_Large','Rock_1']:
 source={'BirchTree_1':'BirchTree_2','BirchTree_2':'BirchTree_3'}.get(name,name);obj=bpy.data.objects.get(source);obj.location=(0,0,0)
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 low=min(v.co.z for v in obj.data.vertices)
 for v in obj.data.vertices:v.co.z-=low
 lods(obj,name,[8000,3000,800] if 'Tree' in name else [2000,600,200])


