#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <fstream>
#include <iomanip>

struct Macro { string name; int horizon; };

static int baselineChoice(Sim &sm){
    bool h=sm.harvestMode();
    return h?sm.choose(true):sm.chooseLearnedPrune();
}

static bool macroStep(Sim &sm,const string &mode,int step){
    while(sm.inferSweep()){}
    sm.updateDebt();
    if(sm.active.empty()) return false;
    int id=-1; bool h=false;
    if(mode=="nodebt"){
        sm.debt=false;
        h=sm.harvestMode();
        id=h?sm.choose(true):sm.chooseLearnedPrune();
    } else if(mode=="harvest") {
        h=true; id=sm.choose(true);
    } else if(mode=="softmix") {
        id=sm.chooseSoftMix();
    } else if(mode=="h3p1") {
        h=(step%4)!=3;
        id=h?sm.choose(true):sm.chooseLearnedPrune();
    } else {
        h=sm.harvestMode();
        id=h?sm.choose(true):sm.chooseLearnedPrune();
    }
    if(id<0) return false;
    sm.processQ(id,h); sm.turn++;
    return true;
}

static void emitHeader(ofstream &o){
    o << "snapshot,target,req,known,frontier,fratio,pressure,yieldE,deltaE,debt,closed_frac,sat_frac,inferred_frac,pruneEff,frontPeak,action,horizon,total,delta,complete\n";
}

static void emitRow(ofstream&o,Sim&s,int snap,double target,const Macro&m,int total,int baseline,bool complete){
    double denom=max(1,s.req);
    o<<snap<<','<<target<<','<<s.req<<','<<s.knownNames.size()<<','<<s.frontierSize()<<','
     <<s.fratio()<<','<<s.pressure()<<','<<s.yieldE<<','<<s.deltaE<<','<<(s.debt?1:0)<<','
     <<s.closedq/denom<<','<<s.satq/denom<<','<<s.inferred/denom<<','<<s.pruneEff<<','<<s.frontPeak<<','
     <<m.name<<','<<m.horizon<<','<<total<<','<<(complete?total-baseline:999999)<<','<<(complete?1:0)<<'\n';
}

static bool evalSnapshot(Sim &sm,int snap,double target,ofstream &out){
    Sim base=sm; base.repairIterators();
    Result br=base.run();
    if(!br.complete) return false;
    Macro bm{"baseline",0};
    emitRow(out,sm,snap,target,bm,br.queries,br.queries,true);
    const vector<string> modes={"nodebt","harvest","softmix","h3p1"};
    const vector<int> horizons={50,100};
    for(auto &mode:modes) for(int H:horizons){
        Sim c=sm; c.repairIterators();
        int done=0;
        for(;done<H && !c.active.empty();++done) if(!macroStep(c,mode,done)) break;
        Result rr=c.run();
        emitRow(out,sm,snap,target,{mode,H},rr.queries,br.queries,rr.complete);
    }
    return true;
}

int main(int argc,char **argv){
    if(argc<3){cerr<<"usage: collect_macro_rollouts WORLD.tsv OUT.csv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim sm(w,p,"learnedprune");
    ofstream out(argv[2]); if(!out) return 2;
    out<<setprecision(12); emitHeader(out);
    const vector<double> targets={.40,.55,.70,.80,.90,.95,.99};
    int ti=0;
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;
        double frac=sm.knownNames.size()/(double)w.tags.size();
        while(ti<(int)targets.size() && frac>=targets[ti]){
            if(!evalSnapshot(sm,ti,targets[ti],out)){cerr<<"incomplete baseline continuation\n";return 3;}
            ++ti;
        }
        int id=baselineChoice(sm); if(id<0) break;
        bool h=sm.harvestMode(); sm.processQ(id,h); sm.turn++;
    }
    Result fin=sm.run();
    cout<<"MACRO_SUMMARY snapshots="<<ti<<" final_queries="<<fin.queries<<" complete="<<(fin.complete?1:0)<<"\n";
    return (fin.complete && ti==(int)targets.size())?0:3;
}
