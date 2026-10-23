// SpiritVFX/SpiritCore.hlsl : généré à partir du même code que l'aperçu HTML (Spirit Portal.html). Inclure après Core.hlsl.
#ifndef SPIRIT_CORE_INCLUDED
#define SPIRIT_CORE_INCLUDED
CBUFFER_START(UnityPerMaterial)
    float4 _Hot, _Mid, _Deep, _Size, _Spirit;
    float _Open, _Crack, _Flash, _Breath, _Seed, _Parallax, _Intensity, _Wave;
CBUFFER_END
float ssm(float x){ return x*x*(3.-2.*x); }
float sq(float x){ return x*x; }
float h21(float2 p){ p=frac(p*float2(123.34,456.21)); p+=dot(p,p+float2(45.32,45.32)); return frac(p.x*p.y); }
float2 h22(float2 p){ float n=h21(p); return float2(n,h21(p+float2(n+19.19,n+7.7))); }
float vn(float2 p){ float2 i=floor(p); float2 f=frac(p); f=f*f*(3.-2.*f);
 return lerp(lerp(h21(i),h21(i+float2(1.,0.)),f.x),lerp(h21(i+float2(0.,1.)),h21(i+float2(1.,1.)),f.x),f.y); }
float fbm3(float2 p){ float a=.5; float s=0.; for(int k=0;k<3;k++){ s+=a*vn(p); p=p*2.03+float2(17.1,9.7); a*=.5; } return s; }
float fbm(float2 p){ float a=.5; float s=0.; for(int k=0;k<5;k++){ s+=a*vn(p); p=p*2.03+float2(17.1,9.7); a*=.5; } return s; }
float ridge(float2 p){ float a=.5; float s=0.; for(int k=0;k<4;k++){ s+=a*(1.-abs(vn(p)*2.-1.)); p=p*2.1+float2(3.3,7.1); a*=.5; } return s; }

float3 sporesL(float2 p, float2 sl, float t){
 float3 e=float3(0.,0.,0.);
 for(int L=0;L<3;L++){ float fl=((float)L); float z=.25+fl*.45; float2 pp=(p-sl*z)*(3.5+fl*2.5); pp.y-=t*(.18+fl*.08); pp.x+=sin(pp.y*.9+fl*2.+t*.3)*.5;
  float2 g=floor(pp); float2 f=frac(pp)-float2(.5,.5); float h=h21(g+float2(fl*13.,fl*7.)); float2 o=(h22(g+float2(fl,fl*3.))-float2(.5,.5))*.5; float2 dv=f-o; float dd=dot(dv,dv); float tw=.5+.5*sin(t*1.6+h*30.);
  e+=lerp(_Mid.rgb,_Hot.rgb,h)*step(.5,h)*exp(-dd*(70.-fl*14.))*tw*(.5+fl*.2); }
 return e; }

float3 forest(float2 p, float2 sl, float t){
 float2 ps=p-sl*.9;
 float gl=exp(-length(ps-float2(.03,.12))*1.5);
 float3 fogc=_Deep.rgb*1.1+_Mid.rgb*.1*gl+_Hot.rgb*.04*gl*gl;
 float3 c=fogc;
 float cn=ps.y*.55+(fbm3(ps*float2(1.6,2.4)+float2(t*.02,0.))-.5)*.7;
 c=lerp(c,_Deep.rgb*.25,smoothstep(.15,.65,cn));
 for(int k=0;k<3;k++){ float fl=2.-((float)k); float z=.35+fl*.55; float per=.62-fl*.14; float2 pt=p-sl*z;
  float id=floor(pt.x/per); float lx=(frac(pt.x/per)-.5)*per+sin(pt.y*1.7+id*3.)*.02;
  float hh=h21(float2(id,fl*5.+1.3)); float w=(.025+.06*hh)*(1.+exp(-(pt.y+.95)*5.)*1.2)*(1.+fl*.35);
  float on=step(.28,hh); float cov=on*(1.-smoothstep(w-.006,w+.006,abs(lx)));
  float rim=exp(-sq((abs(lx)-w)*40.))*on*step(abs(lx),w+.02);
  float bark=.7+.6*vn(float2(lx*60.+id*7.,pt.y*3.));
  float fg=clamp(.05+.28*fl,0.,.8);
  float3 tc=_Deep.rgb*.5*bark;
  c=lerp(c,lerp(tc,fogc,fg),cov);
  c+=_Mid.rgb*.35*rim*(1.-fg); }
 float ray=pow(vn(float2((ps.x-ps.y*.6)*5.+t*.04,3.)),2.5)*smoothstep(-.6,.7,ps.y)*(1.-smoothstep(.7,1.2,ps.y));
 c+=_Hot.rgb*.08*ray+_Mid.rgb*.05*ray;
 float ms=exp(-sq((ps.y+.68)*3.))*(.6+.6*fbm3(float2(ps.x*1.6+t*.05,ps.y*4.)));
 c+=_Mid.rgb*.07*ms+_Hot.rgb*.02*ms;
 float gnd=1.-smoothstep(-.9,-.8,ps.y+(fbm3(float2(ps.x*3.,0.))-.5)*.1);
 c=lerp(c,_Deep.rgb*.5+_Mid.rgb*.06*ms,gnd);
 for(int i=0;i<4;i++){ float fi=((float)i); for(int j=0;j<3;j++){ float tj=t-((float)j)*.22;
   float2 wp=float2(sin(tj*.17*(fi+1.)+fi*2.3)*.55,.1+.35*sin(tj*.23*(fi+1.)+fi))-sl*(.3+.1*fi);
   float2 dw=p-wp; float d2=dot(dw,dw); float amp=1.-((float)j)*.32;
   c+=lerp(_Mid.rgb,_Hot.rgb,.35+.3*sin(fi))*exp(-d2*(160.+((float)j)*80.))*.4*amp+_Hot.rgb*exp(-d2*1400.)*.45*amp*step(((float)j),.5); } }
 c+=sporesL(p,sl,t);
 return c; }

float4 spiritFx(float2 m, float3 vt){
 float hh=_Spirit.y*.5; float asp=_Spirit.x/_Spirit.y;
 float2 p=m/hh; float2 q=p/float2(asp,1.); float t=_Time.y;
 float a=atan2(q.y,q.x); float2 ca=float2(cos(a),sin(a)); float r=length(q);
 float o=clamp(_Open,0.,1.); float oe=ssm(o)+max(_Open-1.,0.)*2.5; float oc=clamp(oe,0.,1.);
 float hb=(pow(max(0.,sin(t*2.6)),8.)*.6+pow(max(0.,sin(t*2.6-.7)),10.)*.35)*_Breath;
 float jr=(fbm3(ca*1.6+float2(_Seed,0.))-.5)*.16+(vn(ca*6.+float2(_Seed,t*.15))-.5)*.05;
 float dr=1.+hb*.012+_Breath*.008*sin(t*1.3)-r+jr;
 float th=.14+.16*fbm3(ca*1.2+float2(_Seed+4.,1.));
 float ab=acos(clamp(-ca.y,-1.,1.))/3.14159;
 float rg=smoothstep(.45,1.,_Crack)*1.15;
 float growM=1.-smoothstep(rg-.12,rg,ab+(vn(ca*3.+float2(_Seed,0.))-.5)*.1);
 float fw2=max(fwidth(dr),.002);
 float rm=smoothstep(-th-fw2,-th,dr)*(1.-smoothstep(0.,fw2*1.5,dr));
 float sx=lerp(.04,1.,pow(max(oe,0.),1.5)); float sy=lerp(.22,1.,pow(max(oe,0.),.65));
 float jag=lerp(.18,0.,oc*oc);
 float j=(fbm3(ca*1.9+float2(_Seed,t*.2))-.5)*jag*2.+(vn(ca*6.+float2(_Seed,-t*.8))-.5)*jag*.7;
 float rho=length(float2(q.x/sx,q.y/sy));
 float gate=smoothstep(0.,.03,oe);
 float d=1.+hb*.012-rho+jr+j-(1.-gate)*3.;
 float fw=max(fwidth(d),.002);
 float inside=smoothstep(0.,fw*1.5,d);
 float2 sl=vt.xy/max(abs(vt.z),.12); sl=clamp(sl,float2(-1.6,-1.6),float2(1.6,1.6))*_Parallax;
 float3 col=forest(p,sl,t);
 float edgeIn=exp(-max(d,0.)*9.); float edgeOut=exp(-max(-d,0.)*9.);
 col+=lerp(_Mid.rgb,_Hot.rgb,.5)*edgeIn*(.35+.5*hb)*(1.+_Flash);
 col+=_Hot.rgb*_Flash*.2;
 float rd=max(-dr,0.);
 float bk=ridge(float2(ca.x*3.+rd*30.,ca.y*3.+rd*12.+_Seed))*.7+.3*ridge(float2(ca.x*8.+rd*8.,ca.y*8.+_Seed));
 float bk2=fbm3(float2(ca.x*7.+rd*50.,ca.y*7.+rd*20.));
 float3 barkc=lerp(_Deep.rgb*1.2+float3(.006,.004,.003),float3(.17,.1,.055),smoothstep(.35,.85,bk))*(.35+1.1*bk2);
 float ms=smoothstep(.5,.75,fbm3(ca*3.2+float2(rd*4.,_Seed*.3)));
 barkc=lerp(barkc,float3(.03,.12,.04)*(.6+bk2),ms*.8);
 float ch=1.-smoothstep(0.,.022,abs(fbm3(ca*2.5+float2(rd*5.,_Seed+9.))-.5));
 float flow=.5+.5*sin(a*4.-t*2.4+rd*6.);
 float3 sap=_Mid.rgb*ch*(.35+.8*flow+hb*1.1);
 float rim=exp(-rd*10.)*(.35+hb*.8+_Flash);
 float3 ringCol=barkc*(1.-.5*smoothstep(.3,1.,rd/th))+sap*.7+lerp(_Mid.rgb,_Hot.rgb,.5)*rim*.25;
 float2 w2=float2(fbm3(p*1.3+float2(_Seed,_Seed)),fbm3(p*1.3+float2(_Seed+5.,_Seed+5.)))-float2(.5,.5);
 float vr=ridge(p*2.4+w2*1.4+float2(_Seed,0.)); float vein=pow(vr,9.)*1.1;
 float pulse2=.5+.5*sin(length(p)*9.-t*3.2);
 float3 memCol=_Deep.rgb*1.2*(.4+fbm3(p*3.+float2(t*.03,0.)))+lerp(_Mid.rgb,_Hot.rgb,smoothstep(.85,1.,vr))*vein*(.3+.5*pulse2+hb*.8)*_Crack+_Hot.rgb*_Flash*.3*vein+lerp(_Mid.rgb,_Hot.rgb,.5)*edgeOut*gate*.9;
 float memA=smoothstep(.55,1.,_Crack);
 float aR=rm*growM; float aM=memA*(1.-rm)*step(0.,dr);
 float3 baseRGB=ringCol*aR+memCol*aM; float baseA=max(aR*.97,aM*.92);
 float grow=max(_Crack,oc);
 float rc=length(p*float2(.8,.9));
 float2 w=float2(fbm3(p*1.1+float2(_Seed,_Seed)),fbm3(p*1.1+float2(_Seed+7.,_Seed+3.)))-float2(.5,.5);
 float l1=ridge(p*1.5+w*1.5+float2(_Seed,0.)); float l2=ridge(p*4.+w*2.2+float2(0.,_Seed*2.));
 float ln=max(pow(l1,7.)*2.,pow(l2,9.)*1.5*smoothstep(0.,.6,grow));
 float reach=grow*2.6+(fbm3(p*2.+float2(t*.1,0.))-.5)*.5;
 float spread=1.-smoothstep(reach-.5,reach,rc);
 float fade=exp(-rc*.55);
 float pulse=.55+.45*sin(rc*7.-t*3.);
 float3 outc=lerp(_Mid.rgb,_Hot.rgb,smoothstep(.8,1.,l1))*ln*spread*fade*pulse*(1.+_Flash*2.)*.9;
 outc*=smoothstep(0.,.08,-dr-th);
 float ro=max(r-1.,0.);
 float rays=pow(vn(ca*4.+float2(t*.2,_Seed)),2.)*exp(-ro*1.4)*_Flash;
 float oz=smoothstep(0.,.1,-dr-th);
 outc+=lerp(_Mid.rgb,_Hot.rgb,.6)*rays*.6*oz;
 outc+=lerp(_Mid.rgb,_Hot.rgb,.3)*exp(-max(-dr-th,0.)*5.)*oc*(.22+.12*hb)*oz;
 float qe=length(m/(_Size.xy*.5)); outc*=1.-smoothstep(.12,.8,qe);
 float3 rgb=col*inside+(1.-inside)*(baseRGB+outc);
 float alpha=inside+(1.-inside)*baseA;
 return float4(rgb*_Intensity,alpha); }

float4 groundFx(float2 m){
 float hh=_Spirit.y*.5; float2 p=m/hh; float t=_Time.y;
 float o=ssm(clamp(_Open,0.,1.)); float grow=max(_Crack,o);
 float rc=length(p*float2(.9,1.));
 float2 w=float2(fbm3(p+float2(_Seed+3.,_Seed+3.)),fbm3(p+float2(_Seed+9.,_Seed+9.)))-float2(.5,.5);
 float l1=ridge(p*1.3+w*1.4+float2(_Seed,0.)); float l2=ridge(p*3.6+w*2.+float2(0.,_Seed*2.));
 float ln=max(pow(l1,7.)*2.,pow(l2,9.)*1.4*grow);
 float reach=grow*2.4+(fbm3(p*1.7)-.5)*.6;
 float spread=1.-smoothstep(reach-.5,reach,rc);
 float fade=exp(-rc*.7);
 float pulse=.55+.45*sin(rc*6.-t*2.8);
 float3 cc=lerp(_Mid.rgb,_Hot.rgb,smoothstep(.8,1.,l1))*ln*spread*fade*pulse*(1.+_Flash*2.);
 float pool=exp(-length(p*float2(.55,1.))*1.3)*o*(.7+.3*vn(p*3.+float2(t*.3,0.)));
 cc+=_Mid.rgb*pool*.45+_Hot.rgb*pool*pool*.15;
 float wr=_Wave*3.4; float ring=exp(-sq((rc-wr)*5.5))*(1.-_Wave)*step(.001,_Wave);
 cc+=lerp(_Mid.rgb,_Hot.rgb,.5)*ring*1.0;
 float scorch=(1.-smoothstep(.2,2.4,rc))*grow*.3*(.6+.4*fbm3(p*3.));
 float fe=1.-smoothstep(.3,.85,length(m/(_Size.xy*.5))); fe*=fe;
 return float4(cc*fe,scorch*fe); }

float heatMask(float2 m){
 float hh=_Spirit.y*.5; float2 p=m/hh; float asp=_Spirit.x/_Spirit.y; float2 q=p/float2(asp,1.); float r=length(q);
 float o=ssm(clamp(_Open,0.,1.));
 float k=exp(-sq(max(r-.85,0.)*1.5))*o*(1.-smoothstep(1.7,2.4,r))*.5;
 float wr=_Wave*2.4; k+=exp(-sq((r-wr)*3.5))*(1.-_Wave)*step(.001,_Wave)*.5;
 return k; }

#endif
