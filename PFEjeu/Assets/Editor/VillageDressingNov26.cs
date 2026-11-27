using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.AI;
using UnityEngine.Splines;
using Core.Village;

public static class VillageDressingNov26
{
    static Terrain terrain;
    static Transform root;
    static readonly List<Vector2> routes=new List<Vector2>();
    static readonly List<Vector3> occupied=new List<Vector3>(); // x/z and horizontal radius in y
    static readonly List<GameObject> structures=new List<GameObject>();
    static Vector2[] polygon;
    static VillageArtNov26Builder.Registry registry;
    static float H(float x,float z)=>terrain.SampleHeight(new Vector3(x,0,z))+terrain.transform.position.y;
    static Transform Group(string name){var go=new GameObject(name);go.transform.SetParent(root,false);return go.transform;}
    static VillageArtNov26Builder.Entry Info(int index)=>registry.entries.Single(e=>e.index==index);
    static float Flatness(Vector2 p,float radius)
    {
        float min=float.MaxValue,max=float.MinValue;
        for(int i=0;i<9;i++){float a=i*Mathf.PI/4;var q=i==8?p:p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;float y=H(q.x,q.y);min=Mathf.Min(min,y);max=Mathf.Max(max,y);}return max-min;
    }
    static bool Inside(Vector2 p)
    {
        bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)if((polygon[i].y>p.y)!=(polygon[j].y>p.y)&&p.x<(polygon[j].x-polygon[i].x)*(p.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x)inside=!inside;return inside;
    }
    static float RouteDistance(Vector2 p)=>routes.Count==0?1000:Mathf.Sqrt(routes.Min(v=>(v-p).sqrMagnitude));
    static void Route(Vector3 start,Vector3 end)
    {
        if(!NavMesh.SamplePosition(start,out var a,8,NavMesh.AllAreas)||!NavMesh.SamplePosition(end,out var b,8,NavMesh.AllAreas))return;
        var path=new NavMeshPath();if(!NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path))return;
        for(int i=1;i<path.corners.Length;i++){var p=path.corners[i-1];var q=path.corners[i];int count=Mathf.CeilToInt(Vector3.Distance(p,q));for(int n=0;n<=count;n++){var v=Vector3.Lerp(p,q,n/(float)Mathf.Max(count,1));routes.Add(new Vector2(v.x,v.z));}}
    }
    static GameObject Home(int index,Transform parent,float x,float z,float yaw,string name)
    {
        var info=Info(index);float radius=Mathf.Max(info.width,info.depth)*.55f;
        var initial=new Vector2(x,z);var offsets=new[]{Vector2.zero,new Vector2(3,0),new Vector2(-3,0),new Vector2(0,3),new Vector2(0,-3),new Vector2(5,3),new Vector2(-5,-3),new Vector2(6,-4),new Vector2(-6,4)};
        foreach(var offset in offsets){var p=initial+offset;if(!Inside(p)||Flatness(p,radius)>.7f||RouteDistance(p)<radius+1.2f||occupied.Any(o=>Vector2.Distance(p,new Vector2(o.x,o.z))<radius+o.y+2))continue;
            var go=VillageArtNov26Builder.Place(index,parent,p.x,p.y,yaw,1,name,true);occupied.Add(new Vector3(p.x,radius,p.y));structures.Add(go);return go;
        }
        return null;
    }
    static GameObject Prop(int index,Transform parent,float x,float z,float yaw=0,float scale=1,string name=null,bool solid=false)
        =>VillageArtNov26Builder.Place(index,parent,x,z,yaw,scale,name,solid);
    public static string Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first");
        terrain=Terrain.activeTerrain;registry=JsonUtility.FromJson<VillageArtNov26Builder.Registry>(File.ReadAllText(VillageArtNov26Builder.Work+"/generated.json"));
        if(GameObject.Find("Village_Art_Nov26")!=null)throw new InvalidOperationException("Village dressing already exists; inspect before replacing it.");
        root=new GameObject("Village_Art_Nov26").transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Aménager le village");
        var zone=UnityEngine.Object.FindObjectsByType<SplineContainer>().Single(s=>s.name=="zone village");polygon=Enumerable.Range(0,160).Select(i=>{var p=(Vector3)zone.EvaluatePosition(i/159f);return new Vector2(p.x,p.z);}).ToArray();
        routes.Clear();occupied.Clear();structures.Clear();var start=new Vector3(87,H(87,-110),-110);
        foreach(var b in UnityEngine.Object.FindObjectsByType<Building>()){occupied.Add(new Vector3(b.transform.position.x,6,b.transform.position.z));Route(start,b.transform.position);}
        foreach(var p in new[]{new Vector2(-62,-142),new Vector2(-52,-112),new Vector2(-7,-150),new Vector2(64,-149),new Vector2(12,-90)})Route(start,new Vector3(p.x,H(p.x,p.y),p.y));
        var high=Group("01_Quartier_des_hauteurs");var middle=Group("02_Rue_des_artisans");var low=Group("03_Habitations_et_place_basse");var forge=Group("04_Atelier_du_forgeron");var tavern=Group("05_Terrasse_de_la_taverne");var farm=Group("06_Cour_de_la_ferme");var wood=Group("07_Scierie_et_carriere");var training=Group("08_Camp_et_commandement");var expedition=Group("09_Depart_des_expeditions");var nature=Group("10_Jardins_et_arbres");
        Home(16,high,-57,-114,80,"Maison haute · Sauge");Home(9,high,-65,-135,60,"Maison haute · Auvent");Home(18,high,-57,-151,65,"Logis des hauteurs");Home(16,high,-35,-155,25,"Maison des anciens");Home(12,high,-72,-112,100,"Petite tente des hauteurs");Home(9,high,-74,-145,80,"Maison du rempart haut");
        Home(16,middle,-39,-91,100,"Maison des artisans");Home(18,middle,-15,-101,120,"Tente de l’artisan");Home(9,middle,-7,-145,60,"Habitation de la terrasse");Home(12,middle,-1,-162,10,"Tente du belvédère");Home(16,middle,19,-143,310,"Maison du belvédère");Home(9,middle,-4,-102,100,"Échoppe couverte");
        Home(9,low,65,-140,330,"Maison de la place basse");Home(16,low,81,-151,310,"Grande yourte de la place basse");Home(18,low,49,-153,40,"Tente des voyageurs");Home(12,low,62,-162,10,"Petite maison du rempart");Home(9,low,83,-136,300,"Maison du quartier de la porte");Home(16,low,43,-136,50,"Habitation du chemin bas");
        // Real workstations around the smith, kept outside the building's body and the stairs.
        Prop(70,forge,-4.5f,-84.5f,20,1,"Enclume · poste du forgeron",true);Prop(72,forge,-3.5f,-79.4f,110,1,"Meule d’affûtage",true);Prop(71,forge,-4.1f,-75.7f,85,1,"Établi de forge",true);Prop(69,forge,-8.5f,-88.2f,30,.9f,"Bassin de trempe",true);Prop(73,forge,-17.5f,-83.4f,25,.68f,"Soufflet de l’atelier",true);Prop(59,forge,-1.5f,-76.8f,0,.8f,"Caisse de lingots");Prop(63,forge,-2.4f,-73.4f,95,.9f,"Réserve de planches");Prop(42,forge,-10,-88.8f,0,.7f,"Tonneau de l’atelier");
        var smith=GameObject.Find("Forgeron");if(smith!=null){var rs=smith.GetComponentsInChildren<MeshRenderer>();if(rs.Length>0)smith.transform.position+=Vector3.up*(H(smith.transform.position.x,smith.transform.position.z)-rs.Min(r=>r.bounds.min.y));}
        // Shared dining area: benches run along the tables, food remains on the tabletops.
        foreach(float z in new[]{-112.5f,-118.5f}){var table=Prop(41,tavern,29,z,90,1.05f,"Table commune",true);Prop(34,tavern,29,z-1.25f,90,1.1f,"Banc de la table",true);Prop(34,tavern,29,z+1.25f,90,1.1f,"Banc de la table",true);var food=Prop(38,tavern,29.3f,z,25,.45f,"Repas partagé");food.transform.position=new Vector3(food.transform.position.x,LocalTop(table)+.015f,food.transform.position.z);var pot=Prop(64,tavern,28.4f,z,0,.35f,"Vaisselle de terre");pot.transform.position=new Vector3(pot.transform.position.x,LocalTop(table)+.015f,pot.transform.position.z);}
        Prop(37,tavern,24,-125,0,1,"Foyer commun",true);Prop(36,tavern,13,-118,270,.8f,"Cuisine extérieure",true);Prop(39,tavern,13,-116,0,1,"Ustensiles de cuisine");Prop(43,tavern,22,-120.4f,0,.7f,"Tonneau de boisson",true);Prop(40,tavern,15,-120,0,.8f,"Provisions de la taverne");Prop(59,tavern,12,-121,15,.8f,"Cagette de cuisine");
        Prop(66,high,-42,-137,20,1,"Puits de la place haute",true);Prop(35,high,-39,-134,200,.9f,"Siège près du puits");Prop(64,high,-43.5f,-134.5f,0,.7f,"Jarre de la place");Prop(62,high,-20,-138,45,.75f,"Panneau du conseil",true);Prop(65,low,59,-129,130,1,"Puits de la place basse",true);Prop(43,low,61.5f,-128,0,.65f,"Réserve d’eau");
        Prop(67,farm,36,-63,90,1,"Charrette de récolte",true);Prop(40,farm,34,-59,10,.85f,"Sacs de grain");Prop(40,farm,34.8f,-58.5f,80,.72f,"Récolte stockée");Prop(59,farm,34,-56,20,.8f,"Caisse de la ferme");Prop(64,farm,28,-68,0,1,"Jarre du jardin");
        for(int i=0;i<3;i++){float x=21+i*3;for(int n=0;n<3;n++){var sprout=Prop(45,farm,x,-69-n*1.3f,0,.12f,"Plantation du potager");}Prop(63,farm,x-1,-70.3f,0,1,"Bordure du potager");Prop(63,farm,x+1,-70.3f,0,1,"Bordure du potager");}
        for(int i=0;i<6;i++){float x=-44+(i%3)*1.1f;float z=-70-(i/3)*.6f;var log=Prop(57,wood,x,z,15,1.1f,"Bois débité");if(i>=3)log.transform.position+=Vector3.up*.45f;}Prop(58,wood,-42,-75,80,1.2f,"Troncs à débiter");Prop(55,wood,-39,-71,0,1.2f,"Billot de travail",true);Prop(71,wood,-38,-75,100,1,"Établi du bois",true);Prop(67,wood,-43,-80,15,1,"Charrette de la scierie",true);Prop(63,wood,-37,-64,80,1.1f,"Planches sèches");Prop(59,wood,-49,-58,60,1,"Caisse de la carrière",true);Prop(40,wood,-48,-60,0,.75f,"Sacs de minerais");Prop(71,wood,-59,-60,45,.8f,"Établi de la carrière");Prop(55,wood,-58,-53,0,1,"Borne de la carrière");
        // Training lane faces a clear strip; no target sits on the route from the gate to the village.
        for(int i=0;i<3;i++)Prop(i==1?32:31,training,62+i*3,-124,0,1,"Cible d’entraînement "+(i+1),true);
        var shield=Prop(30,training,62,-121,0,1,"Bouclier du camp");shield.transform.rotation=Quaternion.Euler(65,0,0);
        Prop(59,training,71,-120,0,.8f,"Réserve du camp");Prop(61,training,59,-118,85,.8f,"Séchage des cuirs",true);Prop(62,training,72,-111,100,.75f,"Avis du village",true);
        var tableRoot=UnityEngine.Object.FindAnyObjectByType<VillageTableTrigger>();if(tableRoot!=null){var t=tableRoot.transform;foreach(var r in t.GetComponentsInChildren<Renderer>())r.enabled=false;t.localScale=Vector3.one;t.position=new Vector3(84,H(84,-122),-122);var command=Prop(41,t,84,-122,90,1.25f,"Table de commandement",true);var collider=t.GetComponent<BoxCollider>();if(collider!=null){collider.size=new Vector3(4.8f,2.8f,4.8f);collider.center=Vector3.up*1.4f;}var serialized=new SerializedObject(tableRoot);serialized.FindProperty("pressVisual").objectReferenceValue=command.transform;serialized.ApplyModifiedPropertiesWithoutUndo();Prop(62,training,86.8f,-125,270,.8f,"Panneau des défenses",true);}
        Prop(67,expedition,57,-81,25,1,"Charrette d’expédition",true);Prop(60,expedition,55,-73,0,1.4f,"Caisse d’expédition",true);Prop(40,expedition,56.5f,-74,0,1,"Vivres des éclaireurs");Prop(62,expedition,55,-78,120,.7f,"Tableau des destinations",true);Prop(74,expedition,46,-80,180,.8f,"Armoire des éclaireurs");Prop(76,expedition,48,-81,90,.8f,"Comptoir de préparation");Prop(51,expedition,59,-87,60,1,"Direction de la porte",true);
        var trees=new[]{(-74f,-151f,53,1.7f),(-69f,-161f,44,1.8f),(-48f,-152f,45,1.7f),(-77f,-98f,53,1.8f),(-51f,-106f,44,1.4f),(-5f,-159f,45,1.3f),(16f,-155f,44,1.6f),(43f,-148f,45,1.4f),(74f,-166f,53,1.6f),(88f,-145f,44,1.5f),(39f,-67f,45,1.4f),(45f,-74f,44,1.5f),(49f,-91f,53,1.5f)};
        foreach(var tree in trees){var p=new Vector2(tree.Item1,tree.Item2);if(Inside(p)&&RouteDistance(p)>3&&occupied.All(o=>Vector2.Distance(p,new Vector2(o.x,o.z))>o.y+2))Prop(tree.Item3,nature,p.x,p.y,tree.Item1*13,tree.Item4,"Arbre du village");}
        File.WriteAllText(VillageArtNov26Builder.Work+"/routes.json",Newtonsoft.Json.JsonConvert.SerializeObject(routes.Select(p=>new{x=p.x,z=p.y}).ToArray()));
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        return "Village quartiers and workshops placed; "+structures.Count+" additional dwellings, "+routes.Count+" protected path samples.";
    }
    static float LocalTop(GameObject go)=>go.GetComponentsInChildren<Renderer>().Max(r=>r.bounds.max.y);

    static void EnsureContext()
    {
        terrain=Terrain.activeTerrain;root=GameObject.Find("Village_Art_Nov26").transform;registry=JsonUtility.FromJson<VillageArtNov26Builder.Registry>(File.ReadAllText(VillageArtNov26Builder.Work+"/generated.json"));
        var zone=UnityEngine.Object.FindObjectsByType<SplineContainer>().Single(s=>s.name=="zone village");polygon=Enumerable.Range(0,160).Select(i=>{var p=(Vector3)zone.EvaluatePosition(i/159f);return new Vector2(p.x,p.z);}).ToArray();
        occupied.Clear();foreach(var b in UnityEngine.Object.FindObjectsByType<Building>())occupied.Add(new Vector3(b.transform.position.x,6,b.transform.position.z));
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh!=null&&int.TryParse(filter.sharedMesh.name.Replace("Village_",""),out int index)&&index<30){var b=filter.GetComponent<Renderer>().bounds;occupied.Add(new Vector3(filter.transform.position.x,Mathf.Max(b.size.x,b.size.z)*.5f,filter.transform.position.z));}
    }
    static Vector2 Key(Vector2 p)=>new Vector2(Mathf.Round(p.x*100)/100,Mathf.Round(p.y*100)/100);
    public static string Barriers()
    {
        EnsureContext(); var previous=root.Find("11_Barrieres_des_terrasses");if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var fences=Group("11_Barrieres_des_terrasses");int placed=0;var segments=new List<(Vector2,Vector2)>();
        const float step=1.25f;float[] levels={32.18f,38.18f};
        foreach(float level in levels){
            segments.Clear();
            for(float z=-174;z<-38;z+=step)for(float x=-86;x<103;x+=step){var corners=new[]{new Vector2(x,z),new Vector2(x+step,z),new Vector2(x+step,z+step),new Vector2(x,z+step)};var ys=corners.Select(p=>H(p.x,p.y)).ToArray();var crossings=new List<Vector2>();for(int edge=0;edge<4;edge++){int next=(edge+1)%4;if((ys[edge]>=level)==(ys[next]>=level))continue;crossings.Add(Vector2.Lerp(corners[edge],corners[next],Mathf.InverseLerp(ys[edge],ys[next],level)));}if(crossings.Count==2)segments.Add((Key(crossings[0]),Key(crossings[1])));}
            var adjacent=new Dictionary<Vector2,List<int>>();for(int i=0;i<segments.Count;i++)foreach(var point in new[]{segments[i].Item1,segments[i].Item2}){if(!adjacent.TryGetValue(point,out var ids))adjacent[point]=ids=new List<int>();ids.Add(i);}var used=new HashSet<int>();
            for(int first=0;first<segments.Count;first++){if(used.Contains(first))continue;var chain=new List<Vector2>();var current=segments[first].Item1;chain.Add(current);int edge=first;while(edge>=0&&!used.Contains(edge)){used.Add(edge);current=segments[edge].Item1==current?segments[edge].Item2:segments[edge].Item1;chain.Add(current);edge=adjacent[current].Where(i=>!used.Contains(i)).DefaultIfEmpty(-1).First();}if(chain.Count<3)continue;
                var samples=new List<Vector2>{chain[0]};float carry=0;for(int i=1;i<chain.Count;i++){var a=chain[i-1];var b=chain[i];float length=Vector2.Distance(a,b);while(carry+length>=2.3f){float take=2.3f-carry;var point=Vector2.Lerp(a,b,take/Mathf.Max(length,.0001f));samples.Add(point);a=point;length=Vector2.Distance(a,b);carry=0;}carry+=length;}
                for(int i=1;i<samples.Count;i++){var a=samples[i-1];var b=samples[i];var center=(a+b)*.5f;if(!Inside(center)||RouteDistance(center)<3.2f||occupied.Any(o=>Vector2.Distance(center,new Vector2(o.x,o.z))<o.y+1))continue;float dx=((H(center.x+1.5f,center.y)-H(center.x-1.5f,center.y))/3);float dz=((H(center.x,center.y+1.5f)-H(center.x,center.y-1.5f))/3);if(Mathf.Atan(Mathf.Sqrt(dx*dx+dz*dz))*Mathf.Rad2Deg<22)continue;
                    var fence=Prop(48,fences,center.x,center.y,Mathf.Atan2(b.x-a.x,b.y-a.y)*Mathf.Rad2Deg,1,"Garde-corps de terrasse",true);var info=Info(48);fence.transform.localScale=new Vector3(1,1,Vector2.Distance(a,b)/info.depth);placed++;
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Upright terrace barriers: "+placed;
    }
}


