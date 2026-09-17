import bpy,math,json,heapq
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from bpy_extras.object_utils import world_to_camera_view
root=Path(__file__).parent;out=root/'Exports';out.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(root/'WisdomTree-authored.blend'))
obj=bpy.data.objects['WisdomTree'];mesh=obj.data
verts=np.array([v.co[:] for v in mesh.vertices]);tags=np.zeros(len(verts),int)
bvh=BVHTree.FromPolygons([Vector(v) for v in verts],[p.vertices[:] for p in mesh.polygons])
scene=bpy.context.scene;scene.render.resolution_x=850;scene.render.resolution_y=1000
bpy.ops.object.camera_add();camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=2.04
# Hand selected semantic regions from front, right, back, left inspection renders.
pods=[[(343,151),(387,158),(274,221),(285,202),(405,264),(594,156),(607,156),(513,283),(310,322),(124,342),(771,373),(138,442),(177,428),(196,447),(298,431),(301,466),(374,502),(230,505),(206,564),(290,551),(432,537),(339,615),(422,614),(498,412),(511,412),(535,410),(577,410),(535,565),(588,612),(671,568),(682,559),(455,940)],[(327,159),(308,201),(405,267),(541,158),(610,149),(621,221),(345,321),(345,341),(533,344),(233,408),(285,412),(296,463),(343,503),(338,613),(455,573),(463,616),(559,615),(602,566),(623,552),(592,438),(561,429),(500,434),(471,412),(449,412),(488,378),(448,440),(415,940)],[(242,156),(254,159),(461,156),(506,153),(563,199),(574,220),(443,266),(332,285),(459,340),(539,321),(724,340),(76,373),(273,410),(312,410),(335,410),(261,610),(427,615),(473,503),(562,554),(620,503),(643,567),(672,429),(708,438),(651,446),(550,435),(551,405),(176,566),(165,558),(314,555),(394,940)],[(239,151),(308,155),(522,156),(541,203),(445,266),(227,225),(355,322),(320,345),(361,370),(502,344),(502,324),(563,410),(617,407),(550,468),(508,503),(562,508),(511,609),(398,442),(360,446),(349,437),(287,433),(257,444),(256,539),(226,557),(249,568),(289,615),(360,565),(374,539),(386,615),(396,568),(664,560),(435,940)]]
leaves=[[(295,103),(330,114),(369,161),(438,70),(444,93),(256,181),(322,194),(374,201),(622,178),(246,246),(199,257),(289,277),(362,320),(418,345),(381,386),(254,386),(246,343),(685,319),(752,340),(613,314),(595,396),(554,471),(306,573),(535,538),(658,541),(457,463)],[(373,19),(397,91),(338,126),(316,141),(287,139),(674,107),(624,108),(565,165),(364,284),(346,272),(351,259),(378,296),(617,285),(600,259),(562,344),(516,350),(518,386),(329,377),(306,380),(310,435),(252,498),(267,478),(181,569),(559,498),(594,502),(470,473),(536,464)],[(487,110),(562,109),(530,94),(431,57),(433,85),(269,183),(268,275),(188,317),(106,339),(526,197),(594,185),(652,254),(559,247),(478,307),(583,346),(596,386),(351,386),(408,348),(538,428),(661,434),(584,487),(393,589),(191,543),(303,540),(440,478)],[(171,108),(222,109),(241,130),(306,132),(438,82),(475,63),(518,137),(536,134),(193,191),(292,270),(269,310),(381,256),(362,291),(487,288),(551,280),(461,340),(353,396),(281,383),(356,377),(525,436),(497,439),(477,487),(608,502),(283,500),(291,537),(512,563),(640,505)]]
selections=[];surface_hits={}
def view(n):
 a=n*math.pi/2+.3;target=Vector((0,0,.93));camera.location=target+Vector((math.sin(a)*4,-math.cos(a)*4,.05));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();bpy.context.view_layer.update()
 p=np.array([world_to_camera_view(scene,camera,Vector(v))[:] for v in verts]);return np.c_[p[:,0]*850,(1-p[:,1])*1000],p[:,2]
def hit(x,y):
 local=Vector(((x/850-.5)*2.04*.85,(.5-y/1000)*2.04,0));origin=camera.matrix_world@local;direction=camera.matrix_world.to_quaternion()@Vector((0,0,-1));return bvh.ray_cast(origin,direction)
for tag,sets in [(1,leaves),(2,pods)]:
 for n,points in enumerate(sets):
  project,depth=view(n)
  for x,y in points:
   p,normal,face,_=hit(x,y)
   if p is None:continue
   projected=world_to_camera_view(scene,camera,p);rx,ry=(13,19) if tag==2 else (15,16)
   chosen=((project[:,0]-x)/rx)**2+((project[:,1]-y)/ry)**2<1
   chosen &= abs(depth-projected.z)<(.065 if tag==2 else .04)
   tags[chosen]=tag;selections.append(dict(view=n,pixel=[x,y],radii=[rx,ry],tag=tag,vertices=int(chosen.sum())))
for p in mesh.polygons:p.material_index=int(np.bincount(tags[list(p.vertices)],minlength=3).argmax())
# Geodesic filaments use actual mesh edges. Disconnected surfaces never get a bridging line.
adj=[[] for _ in verts]
for e in mesh.edges:
 a,b=e.vertices;dist=float(np.linalg.norm(verts[a]-verts[b]));adj[a].append((b,dist));adj[b].append((a,dist))
def path(a,b):
 q=[(0,a)];dist={a:0};parent={}
 while q:
  cost,v=heapq.heappop(q)
  if cost!=dist[v]:continue
  if v==b:
   result=[b]
   while v!=a:v=parent[v];result.append(v)
   return list(reversed(result))
  for w,length in adj[v]:
   new=cost+length
   if new<dist.get(w,1e9):dist[w]=new;parent[w]=v;heapq.heappush(q,(new,w))
 return []
chains=[]
for n,points in [(0,[(424,845),(445,740),(438,624),(442,540),(442,450),(417,366),(386,319),(353,246),(326,202)]),(0,[(445,540),(504,504),(553,514),(579,552)]),(2,[(404,848),(395,757),(408,646),(399,541),(391,427),(404,346),(435,224)])]:
 view(n);indices=[]
 for x,y in points:
  p,normal,face,_=hit(x,y)
  if p is not None:indices.append(min(mesh.polygons[face].vertices,key=lambda i:(Vector(verts[i])-p).length))
 for a,b in zip(indices,indices[1:]):
  ids=path(a,b)
  if len(ids)<3:continue
  line=[]
  for i in ids:
   p=Vector(verts[i])+mesh.vertices[i].normal*.0018
   line.append(dict(x=p.x,y=p.z,z=-p.y))
  chains.append(dict(points=line))
(out/'WisdomFilaments.json').write_text(json.dumps(dict(chains=chains)))
(root/'wisdom-semantic-selections.json').write_text(json.dumps(selections,indent=2))
bpy.data.objects.remove(camera,do_unlink=True)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'WisdomTree-authored.blend'))
bpy.ops.object.select_all(action='DESELECT');copies=[]
for n,target in enumerate([120000,40000,12000]):
 copy=obj.copy();copy.data=obj.data.copy();bpy.context.collection.objects.link(copy);copy.name='WisdomTree_LOD'+str(n);bpy.context.view_layer.objects.active=copy;copy.select_set(True)
 if n:
  dec=copy.modifiers.new('Silhouette reduction','DECIMATE');dec.ratio=target/len(mesh.polygons);bpy.ops.object.modifier_apply(modifier=dec.name)
 copies.append(copy);copy.select_set(False)
for copy in copies:copy.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(out/'WisdomTree.fbx'),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='STRIP',object_types={'MESH'})
print('SEMANTIC_REFINEMENT',len(selections),'filaments',len(chains))

