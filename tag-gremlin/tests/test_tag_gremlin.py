import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import tag_gremlin as tg


FIXTURE = """
<!doctype html>
<html>
<body>
<div>332228 tags</div>
<a href="tags.php?page=2">101-200</a>
<a href="tags.php?page=3323">Last &gt;&gt;</a>

<table>
<tr><td>TagID</td><td>Tag</td><td>Uses</td><td colspan="2">Votes</td><td>Synonyms</td></tr>
<tr><td>178</td><td>alpha</td><td>482,121</td><td>+3,704,177</td><td>-2,334</td><td>2 [+]</td></tr>
<tr style="display:none"><td colspan="5">Synonyms: <a href="?q=a">alpha one</a>, <a href="?q=b">alpha.two</a></td></tr>
<tr><td>18</td><td>beta</td><td>359113</td><td>+2623224</td><td>-3511</td><td>0 [+]</td></tr>
<tr style="display:none"><td colspan="5">No synonyms.</td></tr>
</table>

<table>
<tr><td>TagID</td><td>Tag</td><td>Uses</td><td colspan="2">Votes</td><td>Synonyms</td></tr>
<tr><td>97</td><td>gamma</td><td>70,751</td><td>+539355</td><td>-3775</td><td>1 [+]</td></tr>
<tr style="display:none"><td colspan="5"><a href="?q=g">gamma alias</a></td></tr>
</table>
</body>
</html>
"""


PLAIN_TEXT_FIXTURE = """
<!doctype html>
<html><body>
<div>3 tags</div>
<a href="tags.php?page=1">Last</a>
<table>
<tr><td>TagID</td><td>Tag</td><td>Uses</td><td colspan="2">Votes</td><td>Synonyms</td></tr>
<tr><td>1</td><td>alpha</td><td>10</td><td>+3</td><td>-1</td><td>3 [+]</td></tr>
<tr style="display:none"><td colspan="5">first alias, second_alias, third.alias</td></tr>
<tr><td>2</td><td>beta</td><td>9</td><td>+2</td><td>-0</td><td></td></tr>
<tr style="display:none"><td colspan="5"></td></tr>
<tr><td>3</td><td>gamma</td><td>8</td><td>+1</td><td>-0</td><td>2 [+]</td></tr>
<tr style="display:none"><td colspan="5"><span>one</span><br><span>two</span></td></tr>
</table>
</body></html>
"""


class ParserTests(unittest.TestCase):
    def test_full_page_shape(self):
        parsed = tg.parse_tags_page(FIXTURE)
        self.assertEqual(parsed.reported_total, 332228)
        self.assertEqual(parsed.final_page, 3323)
        self.assertEqual(len(parsed.tags), 3)
        self.assertEqual(parsed.synonym_mismatches, 0)

        alpha = parsed.tags[0]
        self.assertEqual(alpha.tag_id, 178)
        self.assertEqual(alpha.name, "alpha")
        self.assertEqual(alpha.uses, 482121)
        self.assertEqual(alpha.upvotes, 3704177)
        self.assertEqual(alpha.downvotes, 2334)
        self.assertEqual(alpha.reported_synonym_count, 2)
        self.assertEqual(alpha.synonyms, ["alpha one", "alpha.two"])
        self.assertTrue(alpha.synonym_parse_ok)

        beta = parsed.tags[1]
        self.assertEqual(beta.synonyms, [])
        self.assertTrue(beta.synonym_parse_ok)

    def test_synonym_mismatch_is_not_silenced(self):
        html = FIXTURE.replace("<td>2 [+]</td>", "<td>3 [+]</td>", 1)
        parsed = tg.parse_tags_page(html)
        self.assertEqual(parsed.synonym_mismatches, 1)
        self.assertFalse(parsed.tags[0].synonym_parse_ok)

    def test_plain_text_synonyms_and_blank_zero_count(self):
        parsed = tg.parse_tags_page(PLAIN_TEXT_FIXTURE)
        self.assertEqual(len(parsed.tags), 3)
        self.assertEqual(parsed.synonym_mismatches, 0)
        self.assertEqual(
            parsed.tags[0].synonyms,
            ["first alias", "second_alias", "third.alias"],
        )
        self.assertEqual(parsed.tags[1].reported_synonym_count, 0)
        self.assertEqual(parsed.tags[1].synonyms, [])
        self.assertEqual(parsed.tags[2].synonyms, ["one", "two"])


class CliTests(unittest.TestCase):
    def test_numeric_worker_shorthand(self):
        self.assertEqual(
            tg._expand_numeric_worker_shorthand(
                ["harvest", "--url", "https://example.invalid/tags.php", "-8"]
            ),
            [
                "harvest",
                "--url",
                "https://example.invalid/tags.php",
                "--workers",
                "8",
            ],
        )


class DatabaseTests(unittest.TestCase):
    def test_complete_database_and_exports(self):
        parsed = tg.parse_tags_page(FIXTURE)
        parsed.reported_total = 3
        parsed.final_page = 1

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            db_path = root / "test.sqlite3"
            db = tg.open_db(db_path)
            tg.set_meta(db, "reported_total", 3)
            tg.set_meta(db, "final_page", 1)
            tg.set_meta(db, "rows_per_page", 3)
            db.commit()

            tg.write_page(
                db,
                page=1,
                parsed=parsed,
                generation=1,
                attempts=1,
                http_status=200,
                latency_ms=25,
                body_sha256="abc",
            )

            report = tg.verify_db(db)
            self.assertTrue(report["complete"])
            self.assertEqual(report["tag_count"], 3)
            self.assertEqual(
                db.execute("SELECT COUNT(*) FROM tag_synonyms").fetchone()[0],
                3,
            )
            db.close()

            args = type("Args", (), {
                "db": str(db_path),
                "out_dir": str(root / "out"),
                "allow_incomplete": False,
            })()
            self.assertEqual(tg.export_command(args), 0)

            self.assertEqual(
                (root / "out" / "tags.txt").read_text(encoding="utf-8"),
                "alpha\nbeta\ngamma\n",
            )
            synonyms = (root / "out" / "synonyms.tsv").read_text(encoding="utf-8")
            self.assertIn("178\talpha\talpha one", synonyms)
            self.assertIn("97\tgamma\tgamma alias", synonyms)


    def test_tag_id_upsert_is_page_independent(self):
        first = tg.ParsedPage(
            reported_total=1,
            final_page=1,
            tags=[
                tg.TagRecord(
                    tag_id=123,
                    name="old-name",
                    uses=10,
                    upvotes=1,
                    downvotes=0,
                    reported_synonym_count=1,
                    synonyms=["old alias"],
                    synonym_raw_text="old alias",
                    synonym_parse_ok=True,
                )
            ],
            synonym_mismatches=0,
        )
        moved = tg.ParsedPage(
            reported_total=1,
            final_page=1,
            tags=[
                tg.TagRecord(
                    tag_id=123,
                    name="new-name",
                    uses=99,
                    upvotes=4,
                    downvotes=1,
                    reported_synonym_count=1,
                    synonyms=["new alias"],
                    synonym_raw_text="new alias",
                    synonym_parse_ok=True,
                )
            ],
            synonym_mismatches=0,
        )

        with tempfile.TemporaryDirectory() as tmp:
            db = tg.open_db(Path(tmp) / "move.sqlite3")
            tg.write_page(
                db,
                page=10,
                parsed=first,
                generation=1,
                attempts=1,
                http_status=200,
                latency_ms=10,
                body_sha256="old",
            )
            tg.write_page(
                db,
                page=47,
                parsed=moved,
                generation=2,
                attempts=1,
                http_status=200,
                latency_ms=10,
                body_sha256="new",
            )

            self.assertEqual(db.execute("SELECT COUNT(*) FROM tags").fetchone()[0], 1)
            self.assertEqual(
                db.execute(
                    """
                    SELECT name,uses,last_seen_generation
                    FROM tags WHERE tag_id=123
                    """
                ).fetchone(),
                ("new-name", 99, 2),
            )
            self.assertEqual(
                db.execute(
                    "SELECT synonym FROM tag_synonyms WHERE tag_id=123"
                ).fetchall(),
                [("new alias",)],
            )
            columns = {
                row[1] for row in db.execute("PRAGMA table_info(tags)").fetchall()
            }
            self.assertNotIn("source_page", columns)
            db.close()

    def test_complete_refresh_removes_only_unseen_old_tags(self):
        gen1 = tg.ParsedPage(
            reported_total=2,
            final_page=1,
            tags=[
                tg.TagRecord(1, "one", 1, 0, 0, 0, [], "", True),
                tg.TagRecord(2, "two", 1, 0, 0, 0, [], "", True),
            ],
            synonym_mismatches=0,
        )
        gen2 = tg.ParsedPage(
            reported_total=1,
            final_page=1,
            tags=[
                tg.TagRecord(1, "one-renamed", 2, 0, 0, 0, [], "", True),
            ],
            synonym_mismatches=0,
        )

        with tempfile.TemporaryDirectory() as tmp:
            db = tg.open_db(Path(tmp) / "refresh.sqlite3")
            tg.set_meta(db, "reported_total", 2)
            tg.set_meta(db, "final_page", 1)
            tg.set_meta(db, "crawl_generation", 1)
            db.commit()
            tg.write_page(
                db,
                page=1,
                parsed=gen1,
                generation=1,
                attempts=1,
                http_status=200,
                latency_ms=10,
                body_sha256="g1",
            )
            self.assertTrue(tg.verify_db(db)["complete"])
            tg.finalize_generation(db, 1)

            db.execute("DELETE FROM pages")
            tg.set_meta(db, "reported_total", 1)
            tg.set_meta(db, "final_page", 1)
            tg.set_meta(db, "crawl_generation", 2)
            db.commit()
            tg.write_page(
                db,
                page=1,
                parsed=gen2,
                generation=2,
                attempts=1,
                http_status=200,
                latency_ms=10,
                body_sha256="g2",
            )

            report = tg.verify_db(db)
            self.assertTrue(report["complete"])
            self.assertEqual(report["tag_count"], 1)
            self.assertEqual(report["stale_tag_rows"], 1)
            self.assertEqual(tg.finalize_generation(db, 2), 1)
            self.assertEqual(db.execute("SELECT COUNT(*) FROM tags").fetchone()[0], 1)
            self.assertEqual(
                db.execute("SELECT name FROM tags WHERE tag_id=1").fetchone()[0],
                "one-renamed",
            )
            db.close()

    def test_v1_database_migrates_without_losing_tags(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "legacy.sqlite3"
            db = sqlite3.connect(path)
            db.executescript(
                """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO meta(key,value) VALUES('schema_version','1');
                CREATE TABLE pages (
                    page INTEGER PRIMARY KEY,
                    status TEXT NOT NULL,
                    attempts INTEGER NOT NULL DEFAULT 0,
                    http_status INTEGER,
                    latency_ms INTEGER,
                    tag_count INTEGER,
                    synonym_count INTEGER,
                    body_sha256 TEXT,
                    fetched_at TEXT,
                    error TEXT
                );
                CREATE TABLE tags (
                    tag_id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    uses INTEGER NOT NULL,
                    upvotes INTEGER NOT NULL,
                    downvotes INTEGER NOT NULL,
                    reported_synonym_count INTEGER NOT NULL,
                    source_page INTEGER NOT NULL,
                    synonym_raw_text TEXT NOT NULL,
                    synonym_parse_ok INTEGER NOT NULL
                );
                CREATE TABLE tag_synonyms (
                    tag_id INTEGER NOT NULL REFERENCES tags(tag_id) ON DELETE CASCADE,
                    synonym TEXT NOT NULL,
                    PRIMARY KEY (tag_id, synonym)
                );
                INSERT INTO tags VALUES(7,'legacy',12,3,1,1,99,'alias',1);
                INSERT INTO tag_synonyms VALUES(7,'alias');
                """
            )
            db.commit()
            db.close()

            migrated = tg.open_db(path)
            columns = {
                row[1]
                for row in migrated.execute("PRAGMA table_info(tags)").fetchall()
            }
            self.assertNotIn("source_page", columns)
            self.assertIn("last_seen_generation", columns)
            self.assertEqual(
                migrated.execute(
                    "SELECT name,last_seen_generation FROM tags WHERE tag_id=7"
                ).fetchone(),
                ("legacy", 1),
            )
            self.assertEqual(
                migrated.execute(
                    "SELECT synonym FROM tag_synonyms WHERE tag_id=7"
                ).fetchone()[0],
                "alias",
            )
            self.assertEqual(tg.get_meta(migrated, "schema_version"), "2")
            migrated.close()


if __name__ == "__main__":
    unittest.main()
