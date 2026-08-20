#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <deque>
#include <iomanip>

struct QAns { string q; vector<int> ids; };

static QAns answer(World &w,const string &q){
    auto qr=w.query(q); QAns a; a.q=q;
    if(qr.second) for(int i=0;i<qr.first;i++) a.ids.push_back((*qr.second)[i]);
    return a;
}

int main(int argc,char **argv){
    if(argc<2){ cerr<<"usage: certificate_bound WORLD.tsv\n"; return 2; }
    World w=loadWorld(argv[1]);

    Policy p=v1(); p.name="learnedprune";
    Sim sm(w,p,"learnedprune");
    Result base=sm.run();
    if(!base.complete){ cerr<<"baseline incomplete\n"; return 3; }

    deque<string> todo;
    for(char c:ROOT) todo.push_back(string(1,c));
    vector<QAns> saturated, boundary;
    unordered_set<string> seen;
    seen.reserve(w.tags.size()*8);

    while(!todo.empty()){
        string q=move(todo.front()); todo.pop_front();
        if(!seen.insert(q).second) continue;
        auto qr=w.query(q);
        QAns a; a.q=q;
        if(qr.second) for(int i=0;i<qr.first;i++) a.ids.push_back((*qr.second)[i]);
        if(qr.first>=K){
            saturated.push_back(move(a));
            for(char c:NEXT) todo.push_back(q+string(1,c));
        } else boundary.push_back(move(a));
    }

    unordered_map<string,int> bidx;
    bidx.reserve(boundary.size()*2);
    for(int i=0;i<(int)boundary.size();i++) bidx.emplace(boundary[i].q,i);

    vector<int> minimal;
    minimal.reserve(boundary.size());
    for(int i=0;i<(int)boundary.size();i++){
        const string &q=boundary[i].q;
        bool hasSmaller=false;
        for(int a=0;a<(int)q.size() && !hasSmaller;a++){
            for(int z=a+1;z<=(int)q.size();z++){
                if(a==0 && z==(int)q.size()) continue;
                if(bidx.find(q.substr(a,z-a))!=bidx.end()){ hasSmaller=true; break; }
            }
        }
        if(!hasSmaller) minimal.push_back(i);
    }

    vector<char> covered(w.tags.size(),0);
    int coveredTags=0;
    long long minimalReturnMass=0;
    for(int bi:minimal){
        minimalReturnMass += boundary[bi].ids.size();
        for(int id:boundary[bi].ids) if(!covered[id]){covered[id]=1;coveredTags++;}
    }
    int uncovered=(int)w.tags.size()-coveredTags;

    int maxSatGain=0;
    for(auto &a:saturated){
        int g=0; for(int id:a.ids) if(!covered[id]) g++;
        maxSatGain=max(maxSatGain,g);
    }
    int extraLB = uncovered? (uncovered + max(1,maxSatGain)-1)/max(1,maxSatGain) : 0;

    vector<char> greedyCovered=covered;
    int remain=uncovered,greedy=0;
    while(remain>0){
        int best=-1,bg=0;
        for(int i=0;i<(int)saturated.size();i++){
            int g=0; for(int id:saturated[i].ids) if(!greedyCovered[id]) g++;
            if(g>bg){bg=g;best=i;}
        }
        if(best<0 || bg==0){
            cerr<<"unreachable uncovered tags remain="<<remain<<"\n";
            return 4;
        }
        greedy++;
        for(int id:saturated[best].ids) if(!greedyCovered[id]){greedyCovered[id]=1;remain--;}
    }

    long long exactProofFloor=minimal.size();
    long long constructive=exactProofFloor+greedy;
    long long lower=exactProofFloor+extraLB;

    cout<<"CERT_BOUND tags="<<w.tags.size()
        <<" saturated_nodes="<<saturated.size()
        <<" boundary_nodes="<<boundary.size()
        <<" minimal_closed="<<minimal.size()
        <<" minimal_return_mass="<<minimalReturnMass
        <<" tags_covered_by_minimal="<<coveredTags
        <<" uncovered_tags="<<uncovered
        <<" max_saturated_uncovered_gain="<<maxSatGain
        <<" extra_discovery_lb="<<extraLB
        <<" greedy_extra_discovery="<<greedy
        <<" total_lb="<<lower
        <<" omniscient_greedy_total="<<constructive
        <<"\n";
    cout<<"BASELINE queries="<<base.queries
        <<" closed="<<base.closedq
        <<" saturated_network="<<base.satq
        <<" saturated_inferred="<<base.inferred
        <<" structural_saturated="<<saturated.size()
        <<" closed_over_floor="<<(base.closedq-(int)minimal.size())
        <<" gap_to_total_lb="<<(base.queries-lower)
        <<" gap_to_omniscient_greedy="<<(base.queries-constructive)
        <<" complete="<<(base.complete?1:0)
        <<"\n";
    if(base.satq+base.inferred!=(int)saturated.size()){
        cerr<<"saturated accounting mismatch network+inferred="<<(base.satq+base.inferred)
            <<" structural="<<saturated.size()<<"\n";
        return 5;
    }
    return 0;
}
