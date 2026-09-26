#include <algorithm>
#include <array>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <limits>
#include <numeric>
#include <optional>
#include <queue>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace tg {

constexpr std::size_t kResultCap = 40;
constexpr std::string_view kAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789.";

struct Tag {
    std::string name;
    std::uint64_t popularity{};
};

static bool legal_name(std::string_view s) {
    if (s.empty()) return false;
    for (unsigned char c : s) {
        if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.') continue;
        return false;
    }
    return true;
}

static bool better_tag(const Tag& a, const Tag& b) {
    if (a.popularity != b.popularity) return a.popularity > b.popularity;
    return a.name < b.name;
}

class MaxSegTree {
public:
    struct Entry {
        std::uint64_t popularity{};
        std::uint32_t id{std::numeric_limits<std::uint32_t>::max()};
    };

    void build(const std::vector<Tag>& tags, const std::vector<std::uint32_t>& lex_ids) {
        tags_ = &tags;
        n_ = 1;
        while (n_ < lex_ids.size()) n_ <<= 1;
        tree_.assign(n_ * 2, Entry{});
        for (auto& e : tree_) e.id = invalid();
        for (std::size_t i = 0; i < lex_ids.size(); ++i) {
            auto id = lex_ids[i];
            tree_[n_ + i] = Entry{tags[id].popularity, id};
        }
        for (std::size_t i = n_; i-- > 1;) tree_[i] = best(tree_[i << 1], tree_[(i << 1) | 1]);
    }

    Entry range_best(std::size_t l, std::size_t r) const {
        Entry left{}, right{};
        left.id = right.id = invalid();
        l += n_; r += n_;
        while (l < r) {
            if (l & 1) left = best(left, tree_[l++]);
            if (r & 1) right = best(tree_[--r], right);
            l >>= 1; r >>= 1;
        }
        return best(left, right);
    }

    static constexpr std::uint32_t invalid() { return std::numeric_limits<std::uint32_t>::max(); }

private:
    Entry best(const Entry& a, const Entry& b) const {
        if (a.id == invalid()) return b;
        if (b.id == invalid()) return a;
        const auto& ta = (*tags_)[a.id];
        const auto& tb = (*tags_)[b.id];
        return better_tag(ta, tb) ? a : b;
    }

    const std::vector<Tag>* tags_{};
    std::size_t n_{};
    std::vector<Entry> tree_;
};

class Oracle {
public:
    struct QueryResult {
        std::vector<std::uint32_t> ids;
        bool saturated{};
    };

    explicit Oracle(std::vector<Tag> tags) : tags_(std::move(tags)) {
        validate();
        build_indices();
    }

    const std::vector<Tag>& tags() const { return tags_; }

    QueryResult query(std::string_view q) const {
        if (!legal_name(q)) return {};

        const auto [pl, pr] = prefix_range(q);
        const std::size_t prefix_count = pr - pl;

        QueryResult out;
        out.ids.reserve(kResultCap);

        if (prefix_count >= kResultCap) {
            append_prefix_topk(pl, pr, kResultCap, out.ids);
            out.saturated = true;
            return out;
        }

        append_prefix_topk(pl, pr, prefix_count, out.ids);
        if (out.ids.size() == kResultCap) {
            out.saturated = true;
            return out;
        }

        const auto* candidates = substring_candidates(q);
        if (!candidates) {
            out.saturated = false;
            return out;
        }

        for (std::uint32_t id : *candidates) {
            const auto& name = tags_[id].name;
            if (name.size() >= q.size() && std::string_view(name).substr(0, q.size()) == q) continue;
            if (q.size() > 4 && name.find(q) == std::string::npos) continue;
            out.ids.push_back(id);
            if (out.ids.size() == kResultCap) break;
        }
        out.saturated = out.ids.size() == kResultCap;
        return out;
    }

private:
    struct RangeNode {
        std::size_t l{}, r{};
        MaxSegTree::Entry best;
    };

    static int code_char(char c) {
        if (c >= 'a' && c <= 'z') return c - 'a' + 1;
        if (c >= '0' && c <= '9') return c - '0' + 27;
        if (c == '.') return 37;
        return 0;
    }

    static std::uint32_t gram_code(std::string_view s) {
        std::uint32_t x = static_cast<std::uint32_t>(s.size());
        for (char c : s) x = x * 41u + static_cast<std::uint32_t>(code_char(c));
        return x;
    }

    void validate() {
        std::unordered_set<std::string> seen;
        seen.reserve(tags_.size() * 2 + 1);
        for (const auto& t : tags_) {
            if (!legal_name(t.name)) throw std::runtime_error("database contains a tag outside [a-z0-9.]");
            if (!seen.insert(t.name).second) throw std::runtime_error("database contains duplicate tag names");
        }
    }

    void build_indices() {
        const std::size_t n = tags_.size();
        lex_ids_.resize(n);
        std::iota(lex_ids_.begin(), lex_ids_.end(), 0u);
        std::sort(lex_ids_.begin(), lex_ids_.end(), [&](auto a, auto b) { return tags_[a].name < tags_[b].name; });
        seg_.build(tags_, lex_ids_);

        pop_ids_.resize(n);
        std::iota(pop_ids_.begin(), pop_ids_.end(), 0u);
        std::sort(pop_ids_.begin(), pop_ids_.end(), [&](auto a, auto b) { return better_tag(tags_[a], tags_[b]); });

        postings_.reserve(n * 4 + 1);
        for (std::uint32_t id : pop_ids_) {
            const auto& s = tags_[id].name;
            std::unordered_set<std::uint32_t> seen;
            seen.reserve(s.size() * 4 + 1);
            for (std::size_t len = 1; len <= 4; ++len) {
                if (len > s.size()) break;
                for (std::size_t i = 0; i + len <= s.size(); ++i) seen.insert(gram_code(std::string_view(s).substr(i, len)));
            }
            for (auto g : seen) postings_[g].push_back(id);
        }
    }

    std::pair<std::size_t, std::size_t> prefix_range(std::string_view q) const {
        auto less_q = [&](std::uint32_t id, std::string_view needle) { return tags_[id].name < needle; };
        const auto lo_it = std::lower_bound(lex_ids_.begin(), lex_ids_.end(), q, less_q);
        std::string hi(q);
        hi.push_back(static_cast<char>(127));
        const auto hi_it = std::lower_bound(lex_ids_.begin(), lex_ids_.end(), std::string_view(hi), less_q);
        return {static_cast<std::size_t>(lo_it - lex_ids_.begin()), static_cast<std::size_t>(hi_it - lex_ids_.begin())};
    }

    void append_prefix_topk(std::size_t l, std::size_t r, std::size_t k, std::vector<std::uint32_t>& out) const {
        if (l >= r || k == 0) return;
        struct NodeCmp {
            const Oracle* self{};
            bool operator()(const RangeNode& a, const RangeNode& b) const {
                const auto ia = a.best.id, ib = b.best.id;
                if (ia == MaxSegTree::invalid()) return true;
                if (ib == MaxSegTree::invalid()) return false;
                return better_tag(self->tags_[ib], self->tags_[ia]);
            }
        };
        std::priority_queue<RangeNode, std::vector<RangeNode>, NodeCmp> pq(NodeCmp{this});
        auto push_range = [&](std::size_t a, std::size_t b) {
            if (a >= b) return;
            auto be = seg_.range_best(a, b);
            if (be.id != MaxSegTree::invalid()) pq.push(RangeNode{a, b, be});
        };
        push_range(l, r);
        while (!pq.empty() && out.size() < k) {
            auto cur = pq.top(); pq.pop();
            const auto chosen = cur.best.id;
            out.push_back(chosen);
            const auto pos_it = std::lower_bound(lex_ids_.begin() + static_cast<std::ptrdiff_t>(cur.l),
                                                 lex_ids_.begin() + static_cast<std::ptrdiff_t>(cur.r),
                                                 tags_[chosen].name,
                                                 [&](std::uint32_t id, const std::string& name) { return tags_[id].name < name; });
            const auto pos = static_cast<std::size_t>(pos_it - lex_ids_.begin());
            push_range(cur.l, pos);
            push_range(pos + 1, cur.r);
        }
    }

    const std::vector<std::uint32_t>* substring_candidates(std::string_view q) const {
        if (q.empty()) return nullptr;
        if (q.size() <= 4) {
            auto it = postings_.find(gram_code(q));
            return it == postings_.end() ? nullptr : &it->second;
        }
        const std::vector<std::uint32_t>* best = nullptr;
        for (std::size_t i = 0; i + 4 <= q.size(); ++i) {
            auto it = postings_.find(gram_code(q.substr(i, 4)));
            if (it == postings_.end()) return nullptr;
            if (!best || it->second.size() < best->size()) best = &it->second;
        }
        return best;
    }

    std::vector<Tag> tags_;
    std::vector<std::uint32_t> lex_ids_;
    std::vector<std::uint32_t> pop_ids_;
    MaxSegTree seg_;
    std::unordered_map<std::uint32_t, std::vector<std::uint32_t>> postings_;
};

struct BenchResult {
    std::uint64_t requests{};
    std::uint64_t returned_items{};
    std::uint64_t saturated{};
    std::uint64_t closed{};
    std::uint64_t discovered{};
    std::uint64_t max_frontier{};
    bool complete{};
    bool hit_query_limit{};
};

static BenchResult run_exhaustive_prefix(const Oracle& oracle, std::uint64_t max_queries) {
    std::vector<std::string> stack;
    stack.reserve(100000);
    for (char c : kAlphabet) stack.emplace_back(1, c);

    std::unordered_set<std::string> queued;
    queued.reserve(100000);
    for (const auto& q : stack) queued.insert(q);

    std::vector<unsigned char> known(oracle.tags().size(), 0);
    std::uint64_t known_count = 0;
    BenchResult r;
    r.max_frontier = stack.size();

    while (!stack.empty() && r.requests < max_queries) {
        std::string q = std::move(stack.back());
        stack.pop_back();
        auto ans = oracle.query(q);
        ++r.requests;
        r.returned_items += ans.ids.size();
        ans.saturated ? ++r.saturated : ++r.closed;
        for (auto id : ans.ids) {
            if (!known[id]) { known[id] = 1; ++known_count; }
        }
        if (ans.saturated) {
            for (char c : kAlphabet) {
                std::string child = q;
                child.push_back(c);
                if (queued.insert(child).second) stack.push_back(std::move(child));
            }
        }
        r.max_frontier = std::max<std::uint64_t>(r.max_frontier, stack.size());
    }
    r.discovered = known_count;
    r.complete = known_count == oracle.tags().size();
    r.hit_query_limit = !stack.empty() && r.requests >= max_queries;
    return r;
}

static std::vector<Tag> load_tsv(const std::string& path) {
    std::ifstream f(path);
    if (!f) throw std::runtime_error("cannot open database file");
    std::vector<Tag> tags;
    std::string line;
    std::size_t lineno = 0;
    while (std::getline(f, line)) {
        ++lineno;
        if (line.empty() || line[0] == '#') continue;
        const auto tab = line.find('\t');
        if (tab == std::string::npos) throw std::runtime_error("TSV line missing tab at line " + std::to_string(lineno));
        const auto pop_s = line.substr(0, tab);
        const auto name = line.substr(tab + 1);
        std::size_t used = 0;
        const auto pop = std::stoull(pop_s, &used);
        if (used != pop_s.size()) throw std::runtime_error("bad popularity at line " + std::to_string(lineno));
        tags.push_back(Tag{name, pop});
    }
    if (tags.empty()) throw std::runtime_error("database contains no tags");
    return tags;
}

static void print_result_json(const BenchResult& r) {
    std::cout << "{\n"
              << "  \"schema\": 1,\n"
              << "  \"requests\": " << r.requests << ",\n"
              << "  \"returned_items\": " << r.returned_items << ",\n"
              << "  \"saturated\": " << r.saturated << ",\n"
              << "  \"closed\": " << r.closed << ",\n"
              << "  \"discovered\": " << r.discovered << ",\n"
              << "  \"max_frontier\": " << r.max_frontier << ",\n"
              << "  \"complete\": " << (r.complete ? "true" : "false") << ",\n"
              << "  \"hit_query_limit\": " << (r.hit_query_limit ? "true" : "false") << "\n"
              << "}\n";
}

static Oracle::QueryResult brute_query(const Oracle& oracle, std::string_view q) {
    Oracle::QueryResult out;
    if (!legal_name(q)) return out;
    std::vector<std::uint32_t> prefix, rest;
    for (std::uint32_t id = 0; id < oracle.tags().size(); ++id) {
        const auto& t = oracle.tags()[id];
        const auto sv = std::string_view(t.name);
        if (sv.starts_with(q)) prefix.push_back(id);
        else if (sv.find(q) != std::string_view::npos) rest.push_back(id);
    }
    auto cmp = [&](auto a, auto b) { return better_tag(oracle.tags()[a], oracle.tags()[b]); };
    std::sort(prefix.begin(), prefix.end(), cmp);
    std::sort(rest.begin(), rest.end(), cmp);
    for (auto id : prefix) {
        if (out.ids.size() == kResultCap) break;
        out.ids.push_back(id);
    }
    for (auto id : rest) {
        if (out.ids.size() == kResultCap) break;
        out.ids.push_back(id);
    }
    out.saturated = out.ids.size() == kResultCap;
    return out;
}

static void expect(bool cond, const char* msg) {
    if (!cond) throw std::runtime_error(std::string("selftest failed: ") + msg);
}

static void selftest() {
    {
        Oracle o({
            {".hazz", 2},
            {"long.hair", 9000},
            {"black.hair", 7000},
            {"zz.ha.zz", 8000},
            {".ham", 1},
            {"foo..bar", 123},
            {"trailing.", 50},
            {"...", 7},
        });
        auto r = o.query(".ha");
        expect(r.ids.size() == 5, ".ha result count");
        expect(o.tags()[r.ids[0]].name == ".hazz", "prefix group first by popularity");
        expect(o.tags()[r.ids[1]].name == ".ham", "all prefix matches precede substring matches");
        expect(o.tags()[r.ids[2]].name == "long.hair", "substring group popularity order 1");
        expect(o.tags()[r.ids[3]].name == "zz.ha.zz", "substring group popularity order 2");
        expect(o.tags()[r.ids[4]].name == "black.hair", "substring group popularity order 3");
        expect(!r.saturated, "small result not saturated");
        expect(o.query(".").ids.size() == 8, "single period query legal");
        expect(o.query("..").ids.size() == 2, "double period query legal");
        expect(o.query("A").ids.empty(), "uppercase query rejected");
    }
    {
        std::vector<Tag> tags;
        for (int i = 0; i < 45; ++i) {
            tags.push_back(Tag{"a" + std::to_string(i / 10) + std::to_string(i % 10), static_cast<std::uint64_t>(1000 - i)});
        }
        tags.push_back(Tag{"zzza", 999999});
        Oracle o(std::move(tags));
        auto r = o.query("a");
        expect(r.ids.size() == 40, "cap exactly 40");
        expect(r.saturated, "40 is ambiguous/saturated");
        for (auto id : r.ids) expect(o.tags()[id].name.rfind("a", 0) == 0, "40 prefix results suppress substring-only results");
    }
    {
        std::vector<Tag> tags;
        std::uint64_t x = 0x9e3779b97f4a7c15ULL;
        auto rnd = [&]() -> std::uint64_t {
            x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
            return x * 2685821657736338717ULL;
        };
        std::unordered_set<std::string> names;
        while (names.size() < 350) {
            const int len = 1 + static_cast<int>(rnd() % 12);
            std::string name;
            name.reserve(len);
            for (int j = 0; j < len; ++j) name.push_back(kAlphabet[rnd() % kAlphabet.size()]);
            names.insert(std::move(name));
        }
        std::uint64_t pop = 0;
        for (const auto& n : names) tags.push_back(Tag{n, 1 + ((pop++ * 17) % 53)});
        Oracle o(std::move(tags));
        std::vector<std::string> queries = {".", "..", "a", "0", ".a", "a.", "...", "abcde"};
        for (int i = 0; i < 1200; ++i) {
            const int len = 1 + static_cast<int>(rnd() % 8);
            std::string q;
            q.reserve(len);
            for (int j = 0; j < len; ++j) q.push_back(kAlphabet[rnd() % kAlphabet.size()]);
            queries.push_back(std::move(q));
        }
        for (const auto& q : queries) {
            const auto fast = o.query(q);
            const auto slow = brute_query(o, q);
            expect(fast.saturated == slow.saturated, "optimized oracle saturation differs from brute reference");
            expect(fast.ids == slow.ids, "optimized oracle ordering/content differs from brute reference");
        }
    }
    {
        Oracle o({{"a", 9}, {"ab", 8}, {"a.", 7}, {".a", 6}, {"1", 5}, {"..", 4}});
        auto r = run_exhaustive_prefix(o, 10000);
        expect(r.complete, "baseline exhaustive-prefix completeness on tiny world");
        expect(r.discovered == 6, "baseline discovered all tiny tags");
    }
    std::cout << "SELFTEST_OK\n";
}

} // namespace tg

int main(int argc, char** argv) {
    try {
        if (argc == 2 && std::string_view(argv[1]) == "--selftest") {
            tg::selftest();
            return 0;
        }
        if (argc < 3) {
            std::cerr << "usage: tagbench --selftest | tagbench <db.tsv> exhaustive-prefix [max_queries]\n";
            return 2;
        }
        const std::string db = argv[1];
        const std::string strategy = argv[2];
        const std::uint64_t max_queries = argc >= 4 ? std::stoull(argv[3]) : 1000000ULL;
        tg::Oracle oracle(tg::load_tsv(db));
        if (strategy != "exhaustive-prefix") throw std::runtime_error("unknown strategy");
        const auto r = tg::run_exhaustive_prefix(oracle, max_queries);
        tg::print_result_json(r);
        return r.complete ? 0 : 3;
    } catch (const std::exception& e) {
        std::cerr << "tagbench error: " << e.what() << "\n";
        return 1;
    }
}
