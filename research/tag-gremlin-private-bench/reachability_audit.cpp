#define main tagbench_embedded_main
#include "tagbench.cpp"
#undef main

namespace {

struct AuditResult {
    std::uint64_t reachable{};
    std::uint64_t unreachable{};
    std::uint64_t oracle_calls{};
};

AuditResult audit_reachability(const tg::Oracle& oracle) {
    AuditResult a;
    for (std::uint32_t id = 0; id < oracle.tags().size(); ++id) {
        const auto& name = oracle.tags()[id].name;
        bool visible = false;
        std::unordered_set<std::string> seen;
        seen.reserve(name.size() * name.size() / 2 + 1);

        // Longest substrings first. In normal cases the full tag proves reachability in one query.
        for (std::size_t len = name.size(); len >= 1 && !visible; --len) {
            for (std::size_t pos = 0; pos + len <= name.size(); ++pos) {
                std::string q = name.substr(pos, len);
                if (!seen.insert(q).second) continue;
                auto ans = oracle.query(q);
                ++a.oracle_calls;
                if (std::find(ans.ids.begin(), ans.ids.end(), id) != ans.ids.end()) {
                    visible = true;
                    break;
                }
            }
            if (len == 1) break;
        }
        visible ? ++a.reachable : ++a.unreachable;
    }
    return a;
}

} // namespace

int main(int argc, char** argv) {
    try {
        if (argc != 2) {
            std::cerr << "usage: tagbench-audit <db.tsv>\n";
            return 2;
        }
        tg::Oracle oracle(tg::load_tsv(argv[1]));
        const auto a = audit_reachability(oracle);
        std::cout << "{\n"
                  << "  \"schema\": 1,\n"
                  << "  \"reachable\": " << a.reachable << ",\n"
                  << "  \"unreachable\": " << a.unreachable << ",\n"
                  << "  \"oracle_calls\": " << a.oracle_calls << "\n"
                  << "}\n";
        return a.unreachable == 0 ? 0 : 4;
    } catch (...) {
        // No DB-derived exception text is emitted by the audit tool.
        std::cerr << "tagbench-audit failed\n";
        return 1;
    }
}
