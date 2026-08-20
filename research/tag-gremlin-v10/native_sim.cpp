#include <bits/stdc++.h>
using namespace std;
static const int K=40;
static const string ROOT="abcdefghijklmnopqrstuvwxyz0123456789";
static const string NEXT="abcdefghijklmnopqrstuvwxyz0123456789.-";

struct Policy{
 string name="v9";
 double h_lm=9,h_siby=2.2,h_sats=11,h_pref=3,h_depth=1.25;
 double p_gap=1.05,p_bridge=1.8,p_close=26,p_shallow=34,p_lm=3.2,p_sat=6;
 double lm_pref=2.8,lm_cont=1.5,lm_s3=.65,lm_s2=.3,lm_h3=.5,lm_h2=.25;
 double score_weight=1;
 double debt_enter_ratio=.55,debt_exit_ratio=.30; int debt_enter_floor=20000,debt_exit_floor=10000,debt_hard=100000;
 double pressure_growth=.025,pressure_t1=.32,pressure_t2=.50,pressure_t3=.75,growth_t1=2,growth_t2=5,growth_t3=10;
 int pressure_p1=2,pressure_p2=3,pressure_p3=4;
 double yield_t1=1.5,yield_t2=4,yield_t3=8; int yield_h1=2,yield_h2=3,yield_h3=4,lowyield_p=2;
 int harvest_h1=1,harvest_h2=2,harvest_h3=3;
 double age_h=0,age_p=0,debt_cont=0; bool debt_mode_continuous=false;
};
Policy v1(){Policy p;p.name="gremlin-ai-v1";
 p.h_lm=16.12960241525806;p.h_siby=1.321680995116915;p.h_sats=5.577760619510637;p.h_pref=4.621132747141924;p.h_depth=.8565875287038986;
 p.p_gap=.5465752494342551;p.p_bridge=2.6279821280435014;p.p_close=24.091138480072626;p.p_shallow=23.330368921131466;p.p_lm=1.5780261605998511;p.p_sat=5.935769215452535;
 p.lm_pref=2.251407764903304;p.lm_cont=1.127741937891319;p.lm_s3=.498881996504165;p.lm_s2=.43014735028015244;p.lm_h3=.3599863072415311;p.lm_h2=.37656830047469225;
 p.score_weight=0;p.debt_enter_ratio=.35;p.debt_exit_ratio=.18;p.debt_enter_floor=0;p.debt_exit_floor=0;p.debt_hard=1000000000;
 p.pressure_growth=.024654071433919282;p.pressure_t1=.3169945280200911;p.pressure_t2=.43041634810036056;p.pressure_t3=.7263035037723605;
 p.growth_t1=2.8237852032103268;p.growth_t2=3.767273134579702;p.growth_t3=9.642985074571207;p.pressure_p1=1;p.pressure_p2=1;p.pressure_p3=3;
 p.yield_t1=1.0603230630017428;p.yield_t2=6.679806066051587;p.yield_t3=7.698181211484613;p.yield_h1=1;p.yield_h2=4;p.yield_h3=7;p.lowyield_p=2;
 p.harvest_h1=0;p.harvest_h2=0;p.harvest_h3=1;p.age_h=3.7792592832917187;p.age_p=1.375139643848606;p.debt_cont=11.47497681945263;return p;}

struct World{
 vector<string> tags; vector<int> scores;
 unordered_map<uint32_t, vector<int>> postings;
 mutable array<int,K> qbuf{};
 static int ccode(char c){
  if(c>='a'&&c<='z') return c-'a'+1;
  if(c>='0'&&c<='9') return c-'0'+27;
  if(c=='.') return 37; if(c=='-') return 38; return 0;
 }
 static uint32_t gcode(string_view s){uint32_t x=(uint32_t)s.size();for(char c:s)x=x*41u+(uint32_t)ccode(c);return x;}
 pair<int,const array<int,K>*> query(const string&q) const{
  if(q.empty()) return {0,nullptr};
  const vector<int>* best=nullptr;
  if(q.size()<=4){auto it=postings.find(gcode(q));if(it==postings.end())return {0,nullptr};best=&it->second;}
  else{
   size_t bs=SIZE_MAX;
   for(size_t i=0;i+4<=q.size();++i){auto it=postings.find(gcode(string_view(q).substr(i,4)));if(it==postings.end())return {0,nullptr};if(it->second.size()<bs){bs=it->second.size();best=&it->second;}}
  }
  int n=0;
  for(int id:*best){if(q.size()<=4 || tags[id].find(q)!=string::npos){qbuf[n++]=id;if(n==K)break;}}
  return n?pair<int,const array<int,K>*>{n,&qbuf}:pair<int,const array<int,K>*>{0,nullptr};
 }
};
vector<string> substrs(const string&s){vector<string> out;out.reserve(s.size()*(s.size()+1)/2);unordered_set<string> seen;for(int a=0;a<(int)s.size();++a)for(int z=a+1;z<=(int)s.size();++z){string q=s.substr(a,z-a);if(seen.insert(q).second)out.push_back(move(q));}return out;}
World loadWorld(const string&path){ifstream f(path);if(!f)throw runtime_error("open world");World w;string line;while(getline(f,line)){auto t=line.find('\t');if(t==string::npos)continue;w.scores.push_back(stoi(line.substr(0,t)));w.tags.push_back(line.substr(t+1));}
 int n=w.tags.size();vector<int> ord(n);iota(ord.begin(),ord.end(),0);sort(ord.begin(),ord.end(),[&](int a,int b){if(w.scores[a]!=w.scores[b])return w.scores[a]>w.scores[b];return w.tags[a]<w.tags[b];});
 w.postings.reserve((size_t)n*8);
 for(int id:ord){auto &t=w.tags[id];unordered_set<uint32_t> seen;seen.reserve(t.size()*4);for(int L=1;L<=4;L++)for(int a=0;a+L<=(int)t.size();a++)seen.insert(World::gcode(string_view(t).substr(a,L)));for(uint32_t g:seen)w.postings[g].push_back(id);}
 return w;}

template<class V> inline V getv(const unordered_map<string,V>&m,const string&k,V d=V{}){auto it=m.find(k);return it==m.end()?d:it->second;}
struct Parent{int n=0,y=0,s=0,c=0;};
struct Cand{string q;bool active=false;int created=0;list<int>::iterator it;list<int>::iterator lit;};
struct Result{int queries=0,found=0,q50=-1,q90=-1,q99=-1,endgame=-1,peak=0;long long area=0;bool complete=false;int inferred=0,closedq=0,satq=0,redundant=0;double meanDepth=0;};

struct Sim{
 const World&w;Policy p;vector<Cand> cs;unordered_map<string,int> cid;list<int> active;vector<list<int>> activeLen;unordered_map<string,vector<int>> fsub;unordered_map<string,int> fsubcnt;
 unordered_set<string> closed,grams,knownNames;unordered_map<string,uint8_t> gramCnt;unordered_map<string,vector<int>> examples;vector<char> knownTag;unordered_map<string,int> subC,subCAll,preC;unordered_map<string,double> ctxMass;unordered_map<string,double> subW,preW;unordered_map<string,Parent> pst;
 deque<int> iq;vector<char> iqflag;
 int turn=0,req=0,processed=0,inferred=0,covered=0,frontPeak=0,frontSamples=0,debtEntries=0;double yieldE=8,deltaE=0;bool debt=false;long long area=0;int closedq=0,satq=0,redundant=0;long long depthSum=0;vector<int> discovery;
 string strategy="classic"; double adAlpha=1.0,adSat=0.1,adInfo=0.0,adAge=.01,pruneEff=4.0; int pruneSamples=0; unordered_map<string,int> parentSupport; unordered_map<string,string> parentMap; double supH=0,supP=0,supZero=0;double lpShadow=3.0,lpSat=1.0; double softTarget=.35,softSlope=6.0;
 Sim(const World&W,Policy P,string strat="classic",double aa=1,double as=.1,double sh=0,double sp=0,double sz=0):w(W),p(P),knownTag(W.tags.size(),0),strategy(strat),adAlpha(aa),adSat(as),supH(sh),supP(sp),supZero(sz),activeLen(64){cid.reserve(W.tags.size()*3);fsub.reserve(W.tags.size()*10);subC.reserve(W.tags.size()*10);subCAll.reserve(W.tags.size()*20);preC.reserve(W.tags.size()*3);for(char c:ROOT)addFrontier(string(1,c),0);frontPeak=active.size();}
 int frontierSize()const{return (int)active.size();}
 bool isCovered(const string&q)const{for(int a=0;a<(int)q.size();++a)for(int z=a+1;z<=(int)q.size();++z)if(closed.count(q.substr(a,z-a)))return true;return false;}
 bool addFrontier(const string&q,int cr){auto gg=gramCnt.find(q);if(gg!=gramCnt.end()){if(gg->second>=K){for(char ch:NEXT){string x=q+string(1,ch);parentMap[x]=q;addFrontier(x,cr);}}return false;}if(isCovered(q))return false;auto it=cid.find(q);if(it!=cid.end())return false;int id=cs.size();cs.push_back({q,true,cr,{},{}});active.push_back(id);auto lit=active.end();--lit;cs[id].it=lit;int L=q.size();if(L>=(int)activeLen.size())activeLen.resize(L+16);activeLen[L].push_back(id);auto ll=activeLen[L].end();--ll;cs[id].lit=ll;cid[q]=id;iqflag.push_back(0);for(auto &x:substrs(q)){fsub[x].push_back(id);fsubcnt[x]++;}if(knownNames.count(q)&&getv(subCAll,q,0)>=K){iq.push_back(id);iqflag[id]=1;}return true;}
 bool removeFrontier(int id){if(id<0||id>=(int)cs.size()||!cs[id].active)return false;auto &c=cs[id];c.active=false;active.erase(c.it);activeLen[c.q.size()].erase(c.lit);for(auto &x:substrs(c.q))fsubcnt[x]--;return true;}
 int pruneClosed(const string&q){int n=0;auto it=fsub.find(q);if(it!=fsub.end())for(int id:it->second)if(removeFrontier(id))++n;covered+=n;return n;}
 double wtag(int c){if(p.score_weight<=0)return 1;double base=1+min(3.0,.6*log10(1+max(0,c)));return 1+p.score_weight*(base-1);}
 void indexTag(int id){auto &n=w.tags[id];double wt=wtag(w.scores[id]);auto ss=substrs(n);int LL=n.size();if(strategy=="learnedprune"||strategy=="softmix"){for(int a=0;a<LL;a++)for(int z=a+1;z<=min(LL,a+4);z++){string qq=n.substr(a,z-a);double m=min(40,(a+1)*(LL-z+1));ctxMass[qq]+=m;}}for(auto &q:ss){int nv=++subCAll[q];auto ci=cid.find(q);if(ci!=cid.end()&&cs[ci->second].active&&knownNames.count(q)&&nv>=K&&!iqflag[ci->second]){iq.push_back(ci->second);iqflag[ci->second]=1;}if(q.size()<=4){subC[q]++;subW[q]+=wt;}}
  for(int L=1;L<=(int)n.size();++L){string q=n.substr(0,L);preC[q]++;preW[q]+=wt;}}
 int addTag(int id){if(knownTag[id])return 0;knownTag[id]=1;knownNames.insert(w.tags[id]);indexTag(id);auto ci=cid.find(w.tags[id]);if(ci!=cid.end()&&cs[ci->second].active&&getv(subCAll,w.tags[id],0)>=K&&!iqflag[ci->second]){iq.push_back(ci->second);iqflag[ci->second]=1;}return 1;}
 void addGram(const string&q,int count,int fresh){grams.insert(q);gramCnt[q]=(uint8_t)count;if(count<K)closed.insert(q);if(q.size()>1){string par;auto pit=parentMap.find(q);par=(pit!=parentMap.end()?pit->second:q.substr(0,q.size()-1));auto &a=pst[par];a.n++;a.y+=fresh;if(count>=K)a.s++;else a.c++;}}
 pair<int,int> debtTh(){return {max(p.debt_enter_floor,(int)llround(knownNames.size()*p.debt_enter_ratio)),max(p.debt_exit_floor,(int)llround(knownNames.size()*p.debt_exit_ratio))};}
 double fratio(){return frontierSize()/(double)max<size_t>(1,knownNames.size());}double pressure(){return max(0.0,min(3.0,fratio()+p.pressure_growth*max(0.0,deltaE)));}
 void updateDebt(){auto[e,x]=debtTh();bool was=debt;if(!was&&(frontierSize()>=e||frontierSize()>=p.debt_hard))debt=true;else if(was&&frontierSize()<=x&&deltaE<=.5)debt=false;if(!was&&debt)debtEntries++;}
 void noteFront(int before,int units=1){double d=(frontierSize()-before)/(double)max(1,units);deltaE=frontSamples?deltaE*.84+d*.16:d;frontSamples+=max(1,units);frontPeak=max(frontPeak,frontierSize());updateDebt();}
 double lm(const string&q){double d=q.size()<=4?getv(subW,q,0.0):0,pre=getv(preW,q,0.0),s2=q.size()>=2?getv(subW,q.substr(q.size()-2),0.0):0,s3=q.size()>=3?getv(subW,q.substr(q.size()-3),0.0):0,h2=q.size()>=2?getv(preW,q.substr(0,2),0.0):0,h3=q.size()>=3?getv(preW,q.substr(0,3),0.0):0;return p.lm_pref*log1p(pre)+p.lm_cont*log1p(d)+p.lm_s3*log1p(s3)+p.lm_s2*log1p(s2)+p.lm_h3*log1p(h3)+p.lm_h2*log1p(h2);}
 struct F{int d,pre,dep,age;double lm,sy,ss,sc,nov,br;};F feat(int id){auto&q=cs[id].q;int d=getv(subCAll,q,0),pre=getv(preC,q,0),dep=q.size(),age=max(0,turn-cs[id].created);double L=lm(q),sy=0,ss=.5,sc=.5;if(q.size()>1){string par;auto pit=parentMap.find(q);par=(pit!=parentMap.end()?pit->second:q.substr(0,q.size()-1));auto it=pst.find(par);if(it!=pst.end()&&it->second.n){sy=it->second.y/(double)it->second.n;ss=it->second.s/(double)it->second.n;sc=it->second.c/(double)it->second.n;}}double nov=1-min(K,d)/(double)K,br=min(K,d)*(K-min(K,d))/(double)K;return {d,pre,dep,age,L,sy,ss,sc,nov,br};}
 double hs(int id){auto f=feat(id);double z=(p.h_lm*f.lm+p.h_siby*f.sy+p.h_sats*f.ss+p.h_pref*log1p(f.pre))*(.45+.55*f.nov)-p.h_depth*(f.dep-1)+p.age_h*log1p(f.age);return z;}
 double ps(int id){auto f=feat(id);double base=p.p_gap*(K-min(K,f.d))+p.p_bridge*f.br+p.p_close*f.sc+p.p_shallow/f.dep-p.p_lm*f.lm-p.p_sat*f.ss+p.age_p*log1p(f.age);if(p.debt_cont)base+=p.debt_cont*pressure()*(1.0/f.dep);return base;}
 pair<int,int> modePlan(){if(debt&&!p.debt_mode_continuous)return {0,1};double pr=pressure(),g=deltaE,y=yieldE;if(pr>=p.pressure_t3||g>=p.growth_t3)return {1,p.pressure_p3};if(pr>=p.pressure_t2||g>=p.growth_t2)return {1,p.pressure_p2};if(pr>=p.pressure_t1||g>=p.growth_t1)return {1,p.pressure_p1};if(y>=p.yield_t3)return {p.yield_h3,1};if(y>=p.yield_t2)return {p.yield_h2,1};if(y>=p.yield_t1)return {p.yield_h1,1};return {1,p.lowyield_p};}
 bool harvestMode(){auto[h,pr]=modePlan();if(!h)return false;return turn%(h+pr)<h;}
 int choose(bool harvest){int mn=1;while(mn<(int)activeLen.size()&&activeLen[mn].empty())mn++;if(mn>=(int)activeLen.size())return -1;int horizon;if(!harvest)horizon=(debt&&!p.debt_mode_continuous)?mn:mn+1;else horizon=mn+(yieldE>=6?p.harvest_h3:yieldE>=2?p.harvest_h2:p.harvest_h1);horizon=min(horizon,(int)activeLen.size()-1);int best=-1;double bs=-1e300;for(int L=mn;L<=horizon;L++)for(int id:activeLen[L]){double z=harvest?hs(id):ps(id);if(z>bs){bs=z;best=id;}}return best;}

 pair<double,double> predictPF(int id,double childFrac,int mn){
  static const double MEAN[20]={2.5107069881487534,.33648038414383585,.2752304064746769,5.276616229297563,1.3267526306004478,.34214817389867636,.6578518261013233,.7516305680425247,.05972828974253818,5.382919267018216,.6055329953551493,.5038971328992988,.9749306272513805,.06375573644527556,.7836931361164153,.36551954057597563,.6442582754393135,.011973845525132816,.09035894292330673,.014640375970576203};
  static const double SCALE[20]={.5630168158997964,.6880088078361996,.6818340723547001,4.4910850925597785,4.445477000846482,.26116776956019394,.26116776956019405,.3563057219356107,.08049197787769453,1.3575011234955072,2.186931886237635,.4545749344738031,2.9086272067179,3.973934669521104,.291218830815401,.36130090163336015,.7578567710854154,.10876797574869951,.2274542209519787,.0805201009868308};
  static const double CC[20]={.6527442646491433,-8.450396111290846,.39311875449053163,-.8143478612258778,-.2862767173877187,.043728092015306204,-.04372809201531492,-.006055208501165116,1.4051196159161246,1.3719087485670844,-.1479884554581822,-.0638633648451946,-.2694968927383952,.07458175862810729,.08595964974424546,-1.6603979496458308,.5585155729232512,-.00156305946552568,1.0226727216822962,.7253159693096268};
  static const double FC[20]={-.146763136937683,-.02683872008984188,.06681766571542545,.0875167950076875,.03472166104992627,.010154944895810439,-.01015494489608071,-.06894428742798385,.018676155453803543,-.09068571067028224,.14887952864519172,-.0009517383082280092,.11502672232128963,-.010958348467807707,.026512921409292376,.07777650482314241,-.015753904742371993,-.032863294041807566,-.07498114611649982,-.03896292988327087};
  auto ff=feat(id); auto&q=cs[id].q; double pf=max(1,getv(fsubcnt,q,1));double dig=0,punc=0;for(char ch:q){dig+=isdigit((unsigned char)ch);punc+=(ch=='.'||ch=='-');}dig/=q.size();punc/=q.size();double x[20]={double(ff.dep),ff.d/40.0,log1p(ff.pre),ff.lm,ff.sy,ff.ss,ff.sc,ff.nov,ff.br/40.0,log1p(ff.age),fratio(),pressure(),yieldE,deltaE,log1p(pf),childFrac,double(ff.dep-mn),knownNames.count(q)?1.0:0.0,dig,punc};double zc=7.6286911892909375,zf=.2166689355423183;for(int i=0;i<20;i++){double z=(x[i]-MEAN[i])/SCALE[i];zc+=CC[i]*z;zf+=FC[i]*z;}double pc=zc>35?1:zc<-35?0:1/(1+exp(-zc));zf=max(-4.0,min(log(41.0),zf));double fresh=max(0.0,min(40.0,expm1(zf)));return {pc,fresh}; }
 int chooseLearnedPrune(){int mn=1;while(mn<(int)activeLen.size()&&activeLen[mn].empty())mn++;if(mn>=(int)activeLen.size())return -1;int horizon=(debt&&!p.debt_mode_continuous)?mn:mn+1;horizon=min(horizon,(int)activeLen.size()-1);int best=-1;double bs=-1e300;for(int L=mn;L<=horizon;L++)for(int id:activeLen[L]){auto&q=cs[id].q;auto [pc,fr]=predictPF(id,.36551954057597563,mn);int pf=max(1,getv(fsubcnt,q,1));double sh=log1p(getv(ctxMass,q,0.0));double score=pc*(pf+lpShadow*sh+12.0/q.size())-lpSat*(1-pc)*14.0+.02*log1p(max(0,turn-cs[id].created));if(score>bs){bs=score;best=id;}}return best;}
 double learnedPruneScore(int id,int mn){auto&q=cs[id].q;auto [pc,fr]=predictPF(id,.36551954057597563,mn);int pf=max(1,getv(fsubcnt,q,1));double sh=log1p(getv(ctxMass,q,0.0));return pc*(pf+lpShadow*sh+12.0/q.size())-lpSat*(1-pc)*14.0+.02*log1p(max(0,turn-cs[id].created));}
 int chooseSoftMix(){
  int mn=1;while(mn<(int)activeLen.size()&&activeLen[mn].empty())mn++;if(mn>=(int)activeLen.size())return -1;
  int horizon=min(mn+3,(int)activeLen.size()-1);struct SV{int id;double h,p;};vector<SV> pool;pool.reserve(2048);
  double mh=0,mp=0;long long nn=0;
  for(int L=mn;L<=horizon;L++)for(int id:activeLen[L]){double hh=hs(id),pp=learnedPruneScore(id,mn);pool.push_back({id,hh,pp});mh+=hh;mp+=pp;nn++;}
  if(!nn)return -1;mh/=nn;mp/=nn;double vh=0,vp=0;
  for(auto &v:pool){double a=v.h-mh,b=v.p-mp;vh+=a*a;vp+=b*b;}double sh=sqrt(vh/max(1LL,nn-1)),sp=sqrt(vp/max(1LL,nn-1));if(sh<1e-9)sh=1;if(sp<1e-9)sp=1;
  double x=softSlope*(fratio()-softTarget)+.10*max(0.0,deltaE)-.055*max(0.0,yieldE-2.0);
  double w=x>30?1:x<-30?0:1/(1+exp(-x));
  int best=-1;double bs=-1e300;
  for(auto &v:pool){int id=v.id;double zh=(v.h-mh)/sh,zp=(v.p-mp)/sp;double z=(1-w)*zh+w*zp+.01*log1p(max(0,turn-cs[id].created));if(z>bs){bs=z;best=id;}}
  return best;
 }

 void addChildrenDir(const string&q,bool left){for(char c:NEXT){string x=left?string(1,c)+q:q+string(1,c);parentMap[x]=q;addFrontier(x,turn);}}
 void addChildren(const string&q){addChildrenDir(q,false);}
 int inferSweep(){int changed=0,before=frontierSize();while(!iq.empty()&&changed<300){int id=iq.front();iq.pop_front();iqflag[id]=0;if(!cs[id].active)continue;string q=cs[id].q;if(!knownNames.count(q)||getv(subCAll,q,0)<K)continue;removeFrontier(id);addGram(q,K,0);inferred++;processed++;addChildren(q);changed++;}if(changed)noteFront(before,changed);return changed;}
 void processQ(int id,bool harvest){string q=cs[id].q;int f0=frontierSize();int before=knownNames.size();auto qr=w.query(q);int count=qr.first;req++;if(qr.second)for(int j=0;j<count;j++)addTag((*qr.second)[j]);int fresh=knownNames.size()-before;removeFrontier(id);addGram(q,count,fresh);processed++;yieldE=processed==1?fresh:yieldE*.82+fresh*.18;depthSum+=q.size();if(count<K){closedq++;int n=pruneClosed(q);noteFront(f0,1);if(fresh==0&&n==0)redundant++;int eff=max(1,f0-frontierSize());pruneEff=pruneSamples?pruneEff*.88+eff*.12:eff;pruneSamples++;}else{satq++;addChildren(q);noteFront(f0,1);}discovery.push_back(knownNames.size());area+=frontierSize();}
 void repairIterators(){for(auto it=active.begin();it!=active.end();++it)cs[*it].it=it;for(int L=0;L<(int)activeLen.size();L++)for(auto it=activeLen[L].begin();it!=activeLen[L].end();++it)cs[*it].lit=it;}
 Result run(int maxq=1000000){while(!active.empty()&&req<maxq){while(inferSweep()){}updateDebt();if(active.empty())break;bool h=true;int id;if(strategy=="softmix"){id=chooseSoftMix();}else if(strategy=="learnedprune"){h=harvestMode();id=h?choose(true):chooseLearnedPrune();}else{h=harvestMode();id=choose(h);}if(id<0)break;processQ(id,h);turn++;}Result r;r.queries=req;r.found=knownNames.size();r.complete=(r.found==(int)w.tags.size()&&active.empty());r.peak=frontPeak;r.area=area;r.inferred=inferred;r.closedq=closedq;r.satq=satq;r.redundant=redundant;r.meanDepth=req?depthSum/(double)req:0;auto qt=[&](double f){int tar=ceil(w.tags.size()*f);for(int i=0;i<(int)discovery.size();++i)if(discovery[i]>=tar)return i+1;return -1;};r.q50=qt(.5);r.q90=qt(.9);r.q99=qt(.99);r.endgame=r.q99<0?-1:req-r.q99;return r;}
};
void printR(const string&name,const Result&r,double sec){cout<<"{\"policy\":\""<<name<<"\",\"queries\":"<<r.queries<<",\"found\":"<<r.found<<",\"complete\":"<<(r.complete?"true":"false")<<",\"q50\":"<<r.q50<<",\"q90\":"<<r.q90<<",\"q99\":"<<r.q99<<",\"endgame\":"<<r.endgame<<",\"frontier_peak\":"<<r.peak<<",\"frontier_area\":"<<r.area<<",\"inferred\":"<<r.inferred<<",\"closed_q\":"<<r.closedq<<",\"sat_q\":"<<r.satq<<",\"redundant\":"<<r.redundant<<",\"mean_depth\":"<<r.meanDepth<<",\"seconds\":"<<sec<<"}\n";}
int main(int argc,char**argv){
 if(argc<3){cerr<<"usage: native_sim WORLD.tsv v9|v1|learnedprune|softmix [params]\n";return 2;}
 auto t=chrono::steady_clock::now();World w=loadWorld(argv[1]);auto t2=chrono::steady_clock::now();
 cerr<<"loaded tags="<<w.tags.size()<<" postings="<<w.postings.size()<<" sec="<<chrono::duration<double>(t2-t).count()<<"\n";
 string mode=argv[2];Policy p;string strategy="classic";
 if(mode=="v1")p=v1();
 else if(mode=="learnedprune"){p=v1();p.name="learnedprune";strategy="learnedprune";}
 else if(mode=="softmix"){p=v1();p.name="softmix";strategy="softmix";}
 else if(mode!="v9"){cerr<<"unknown mode\n";return 2;}
 auto a=chrono::steady_clock::now();Sim sm(w,p,strategy);
 string label=p.name;
 if(mode=="learnedprune"){sm.lpShadow=argc>3?stod(argv[3]):3.0;sm.lpSat=argc>4?stod(argv[4]):1.0;label="learnedprune-s"+to_string(sm.lpShadow)+"-r"+to_string(sm.lpSat);}
 if(mode=="softmix"){sm.softTarget=argc>3?stod(argv[3]):.35;sm.softSlope=argc>4?stod(argv[4]):6.0;label="softmix-t"+to_string(sm.softTarget)+"-s"+to_string(sm.softSlope);}
 auto r=sm.run();auto bb=chrono::steady_clock::now();printR(label,r,chrono::duration<double>(bb-a).count());
 return r.complete?0:3;
}
