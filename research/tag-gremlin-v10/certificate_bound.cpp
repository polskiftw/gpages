#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <deque>
#include <iomanip>

struct QAns { string q; vector<int> ids; };

static bool startsWith(const string &s,const string &p){
    return s.size()>=p.size() && equal(p.begin(),p.end(),s.begin());
}

int main(int argc,char **argv){
    if(argc<2){ cerr<<"usage: certificate_bound WORLD.tsv\n"; return 2; }
    World w=loadWorld(argv[1]);

    Policy p=v1(); p.name="learnedprune";
    Sim sm(w,p,"learnedprune");
    Result base=sm.run();
    if(!base.complete){ cerr<<"baseline incomplete\n"; return 3; }

    unordered_map<string,int> tagId;
    tagId.reserve(w.tags.size()*2);
    for(int i=0;i<(int)w.tags.size();i++) tagId.emplace(w.tags[i],i);

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
    vector<char> isMinimal(boundary.size(),0);
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
        if(!hasSmaller){ minimal.push_back(i); isMinimal[i]=1; }
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

    // Logical saturated-query analysis. A saturated non-tag string can never be
    // inferred by the runtime rule: it must be network-queried. A saturated
    // exact tag may be inferred only after that exact tag was learned elsewhere.
    // For each saturated exact tag, record whether a different query that is
    // not in its descendant subtree can return it. Descendant producers are
    // circular because those queries are not reachable until this node expands.
    vector<int> satTagId(saturated.size(),-1);
    vector<int> satIndexByTag(w.tags.size(),-1);
    int saturatedTagNodes=0,nonTagSaturated=0;
    for(int i=0;i<(int)saturated.size();i++){
        auto it=tagId.find(saturated[i].q);
        if(it==tagId.end()) nonTagSaturated++;
        else { satTagId[i]=it->second; satIndexByTag[it->second]=i; saturatedTagNodes++; }
    }

    vector<char> externalAny(saturated.size(),0);
    vector<char> ancestorProducer(saturated.size(),0);
    vector<char> mandatoryNonTagProducer(saturated.size(),0);
    vector<char> mandatoryClosedProducer(saturated.size(),0);

    auto noteProducer=[&](const QAns &src,bool srcSat,bool srcMinimalClosed){
        bool srcIsTag = tagId.find(src.q)!=tagId.end();
        for(int tid:src.ids){
            int si = (tid>=0 && tid<(int)satIndexByTag.size()) ? satIndexByTag[tid] : -1;
            if(si<0) continue;
            const string &target=saturated[si].q;
            if(src.q==target) continue;
            // If src lies below target in the right-extension trie, target must
            // already have expanded before src can exist, so it cannot rescue it.
            if(src.q.size()>target.size() && startsWith(src.q,target)) continue;
            externalAny[si]=1;
            if(srcSat && src.q.size()<target.size() && startsWith(target,src.q)) ancestorProducer[si]=1;
            if(srcSat && !srcIsTag) mandatoryNonTagProducer[si]=1;
            if(srcMinimalClosed) mandatoryClosedProducer[si]=1;
        }
    };
    for(auto &a:saturated) noteProducer(a,true,false);
    for(int i=0;i<(int)boundary.size();i++) noteProducer(boundary[i],false,isMinimal[i]);

    int external=0,ancestor=0,mandatoryProducer=0,selfOnly=0;
    for(int i=0;i<(int)saturated.size();i++) if(satTagId[i]>=0){
        if(externalAny[i]) external++; else selfOnly++;
        if(ancestorProducer[i]) ancestor++;
        if(mandatoryNonTagProducer[i]||mandatoryClosedProducer[i]) mandatoryProducer++;
    }

    // This is deliberately an optimistic architecture lower bound: it grants
    // every externally discoverable saturated exact-tag node a free inference,
    // even when producers might compete or require an inconvenient ordering.
    // It therefore cannot overstate achievable performance.
    long long logicalSatNetworkLB = nonTagSaturated + selfOnly;
    long long logicalTotalLB = (long long)minimal.size() + logicalSatNetworkLB;
    long long optimisticInferCeiling = external;
    long long remainingInferenceHeadroom=max(0LL,optimisticInferCeiling-(long long)base.inferred);

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
    cout<<"SAT_LOGIC saturated_tag_nodes="<<saturatedTagNodes
        <<" non_tag_saturated="<<nonTagSaturated
        <<" externally_discoverable_non_descendant="<<external
        <<" proper_ancestor_producer="<<ancestor
        <<" mandatory_query_producer="<<mandatoryProducer
        <<" self_only_saturated_tags="<<selfOnly
        <<" optimistic_infer_ceiling="<<optimisticInferCeiling
        <<" current_inferred="<<base.inferred
        <<" remaining_infer_headroom="<<remainingInferenceHeadroom
        <<" logical_sat_network_lb="<<logicalSatNetworkLB
        <<" logical_total_lb="<<logicalTotalLB
        <<" baseline_gap_to_logical_lb="<<(base.queries-logicalTotalLB)
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
    if(base.closedq!=(int)minimal.size()){
        cerr<<"baseline closed count is not at certificate floor\n";
        return 6;
    }
    return 0;
}
