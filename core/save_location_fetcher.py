import requests
import re
import json
import time
import html
import difflib


# Unicode codepoints for trademark/copyright symbols that wikis often omit from page titles
_WIKI_TITLE_STRIP_CHARS = (
    "\u00ae",   # ® registered sign
    "\u2122",   # ™ trade mark
    "\u00a9",   # © copyright
    "\u2018", "\u2019", "\u201c", "\u201d",  # smart quotes (optional: normalize to ASCII)
)


def normalize_wiki_page_title(name):
    """
    Clean a game name for use as a PC Gaming Wiki page title or search query.
    Removes ®, ™, © and similar symbols that the wiki page often doesn't have,
    and normalizes whitespace so we can match pages like "LEGO Star Wars - The Complete Saga".
    """
    if not name or not isinstance(name, str):
        return name
    s = name.strip()
    for char in _WIKI_TITLE_STRIP_CHARS:
        s = s.replace(char, "")
    # Collapse multiple spaces only (keep hyphens so "X - Y" stays intact)
    s = re.sub(r"\s+", " ", s).strip()
    return s


def normalize_for_match(title):
    """
    Canonical form for comparing wiki page titles (letters only, no punctuation).
    'LEGO Star Wars - The Complete Saga' and 'Lego Star Wars: The Complete Saga'
    both become 'lego star wars the complete saga' so we can pick the closest match.
    """
    if not title or not isinstance(title, str):
        return ""
    s = title.strip().lower()
    # Treat subtitle separators the same: "-" and ":" -> space
    for sep in ("-", ":", "–", "—"):
        s = s.replace(sep, " ")
    # Keep only letters, digits, spaces; collapse spaces
    s = re.sub(r"[^a-z0-9\s]", " ", s)
    s = re.sub(r"\s+", " ", s).strip()
    return s


def build_title_variants_for_fallback(clean_name):
    """
    Build wiki-style title variants to try as direct page titles when search fails.
    PC Gaming Wiki often uses "Lego" not "LEGO", ":" not " - ", and underscores in URLs.
    Returns a list of candidate page titles (no duplicates, in try order).
    """
    if not clean_name or not isinstance(clean_name, str):
        return []
    s = clean_name.strip()
    if not s:
        return []
    seen = set()
    out = []
    # 1) Colon instead of " - " (wiki subtitle style)
    if " - " in s:
        v = s.replace(" - ", ": ", 1)
        if v not in seen:
            seen.add(v)
            out.append(v)
    # 2) First word wiki-style (capitalize: "LEGO" -> "Lego"; wiki often uses "Lego" not "LEGO")
    parts = s.split(None, 1)
    if len(parts) == 2 and parts[0]:
        v2 = parts[0].capitalize() + " " + parts[1]
        if v2 not in seen:
            seen.add(v2)
            out.append(v2)
    # 3) Colon + first word wiki-style (e.g. "Lego Star Wars: The Complete Saga")
    for v in list(out):
        if " - " in v:
            vc = v.replace(" - ", ": ", 1)
            if vc not in seen:
                seen.add(vc)
                out.append(vc)
        parts = v.split(None, 1)
        if len(parts) == 2 and parts[0]:
            vc2 = parts[0].capitalize() + " " + parts[1]
            if vc2 not in seen:
                seen.add(vc2)
                out.append(vc2)
    # 4) Slug form (spaces -> underscores; MediaWiki accepts this)
    for v in [s] + out[:]:
        slug = v.replace(" ", "_")
        if slug not in seen:
            seen.add(slug)
            out.append(slug)
    return out


def path_to_directory_only(path_str):
    """
    Normalize a save path from PC Gaming Wiki to directory-only.
    Wiki often appends filename/pattern (e.g. 'SaveGame*.LEGO Star Wars_SavedGame').
    We keep only the directory: everything up to and including the last '\\'.
    Paths like %LOCALAPPDATA%\\...\\SavedGames\\file* are trimmed to ...\\SavedGames\\
    """
    if not path_str or not isinstance(path_str, str):
        return path_str
    path_str = path_str.strip()
    last_backslash = path_str.rfind("\\")
    if last_backslash >= 0:
        return path_str[: last_backslash + 1]
    return path_str


class SaveLocationFetcher:
    BASE_URL = "https://www.pcgamingwiki.com/w/api.php"
    USER_AGENT = "GameSaveBackupTool/1.1"

    def __init__(self):
        self.session = requests.Session()
        self.session.headers.update({'User-Agent': self.USER_AGENT})
        self.last_request_time = 0

    def _rate_limit(self):
        elapsed = time.time() - self.last_request_time
        if elapsed < 0.5: time.sleep(0.5 - elapsed)
        self.last_request_time = time.time()

    def _get_page_name_from_app_id(self, app_id):
        self._rate_limit()
        params = {"action": "query", "list": "search", "srsearch": f"appid:{app_id}", "format": "json"}
        try:
            response = self.session.get(self.BASE_URL, params=params, timeout=20)
            response.raise_for_status()
            data = response.json()
            return data.get("query", {}).get("search", [])[0].get("title")
        except (requests.exceptions.RequestException, IndexError) as e:
            print(f"!! Could not find page by App ID {app_id}: {e}")
            return None

    def _get_page_name_from_search(self, search_title):
        """
        Search wiki by title; return the page title that best matches by letters
        (so "LEGO Star Wars - The Complete Saga" matches "Lego Star Wars: The Complete Saga").
        Tries both " - " and ": " subtitle variants so punctuation doesn't break the match.
        """
        if not search_title or not search_title.strip():
            return None
        search_title = search_title.strip()
        norm_query = normalize_for_match(search_title)
        if not norm_query:
            return None
        # Build search variants: try both "-" and ":" so we find "X: Y" when we have "X - Y"
        variants = [search_title]
        if " - " in search_title:
            variants.append(search_title.replace(" - ", ": ", 1))
        if ": " in search_title and search_title not in variants:
            variants.append(search_title.replace(": ", " - ", 1))
        seen_titles = set()
        all_hits = []
        try:
            for variant in variants[:2]:  # at most 2 requests
                self._rate_limit()
                params = {"action": "query", "list": "search", "srsearch": variant, "format": "json", "srlimit": 15}
                response = self.session.get(self.BASE_URL, params=params, timeout=20)
                response.raise_for_status()
                data = response.json()
                for h in data.get("query", {}).get("search", []):
                    t = h.get("title")
                    if t and t not in seen_titles:
                        seen_titles.add(t)
                        all_hits.append(t)
        except (requests.exceptions.RequestException, KeyError) as e:
            print(f"!! Wiki title search failed for '{search_title}': {e}")
            return None
        # Fallback: if no hits, try a shortened query (e.g. "Lego Star Wars Complete Saga")
        if not all_hits and (" " in search_title or ":" in search_title or "-" in search_title):
            short_query = re.sub(r"\s*(?:[-:])\s*", " ", search_title).strip()
            short_query = re.sub(r"\s+the\s+", " ", short_query, flags=re.IGNORECASE).strip()
            if short_query and short_query != search_title:
                self._rate_limit()
                try:
                    params = {"action": "query", "list": "search", "srsearch": short_query, "format": "json", "srlimit": 15}
                    response = self.session.get(self.BASE_URL, params=params, timeout=20)
                    response.raise_for_status()
                    for h in response.json().get("query", {}).get("search", []):
                        t = h.get("title")
                        if t and t not in seen_titles:
                            seen_titles.add(t)
                            all_hits.append(t)
                except (requests.exceptions.RequestException, KeyError):
                    pass
        if not all_hits:
            return None
        # Pick the hit whose normalized form is closest to our normalized query (letters only)
        best_title = None
        best_ratio = 0.0
        for candidate in all_hits:
            norm_candidate = normalize_for_match(candidate)
            if not norm_candidate:
                continue
            ratio = difflib.SequenceMatcher(None, norm_query, norm_candidate).ratio()
            # Prefer exact normalized match; then prefer candidate that starts with our query
            if norm_candidate == norm_query:
                ratio = max(ratio, 1.0)
            elif norm_candidate.startswith(norm_query) or norm_query.startswith(norm_candidate):
                ratio = max(ratio, 0.85)
            if ratio > best_ratio:
                best_ratio = ratio
                best_title = candidate
        if best_title and best_ratio >= 0.5:
            print(f"--> Wiki title search matched '{search_title}' -> page '{best_title}' (score={best_ratio:.2f})")
            return best_title
        return None

    def _fallback_parse_html_section(self, page_title):
        print(f"--> Attempting HTML parse fallback for '{page_title}'...")
        self._rate_limit()
        try:
            params_sections = {"action": "parse", "page": page_title, "prop": "sections", "format": "json"}
            response_sections = self.session.get(self.BASE_URL, params=params_sections, timeout=20)
            response_sections.raise_for_status()
            sections = response_sections.json().get("parse", {}).get("sections", [])
            save_section_index = next((s.get("index") for s in sections if s.get("line", "").lower() == "save game data location"), None)
            if not save_section_index: return None

            print(f"--> Found 'Save game data location' as section {save_section_index}. Fetching its HTML...")
            self._rate_limit()
            params_html = {"action": "parse", "page": page_title, "prop": "text", "section": save_section_index, "format": "json"}
            response_html = self.session.get(self.BASE_URL, params=params_html, timeout=20)
            response_html.raise_for_status()
            section_html = response_html.json().get("parse", {}).get("text", {}).get("*", "")
            if not section_html: return None

            found_paths = {}
            for platform in ["Steam", "Windows"]:
                match = re.search(rf'{platform}\s*<\/th>\s*<td.*?>(.*?)<\/td>', section_html, re.DOTALL | re.IGNORECASE)
                if match:
                    path_with_tags = match.group(1)
                    path_no_tags = re.sub(r'<.*?>', '', path_with_tags).strip()
                    found_paths[platform.lower()] = path_to_directory_only(html.unescape(path_no_tags))

            print(f"--> Fallback success! Found potential paths: {found_paths}")
            return found_paths if found_paths else None
        except Exception as e:
            print(f"!! Unexpected error during HTML fallback for '{page_title}': {e}")
            return None

    def _fetch_for_page_title(self, page_title):
        """Try semantic then HTML fallback for a single page title; returns paths dict or None."""
        self._rate_limit()
        ask_query = f"[[{page_title}]]|?Has save game location|?Save game data location (Windows)=-|?Save game data location (Steam)=-"
        params = {"action": "ask", "query": ask_query, "format": "json"}
        try:
            response = self.session.get(self.BASE_URL, params=params, timeout=20)
            response.raise_for_status()
            query_results = response.json().get("query", {}).get("results", {})
            if query_results:
                printouts = next(iter(query_results.values())).get("printouts", {})
                win_paths = printouts.get("Save game data location (Windows)", [])
                steam_paths = printouts.get("Save game data location (Steam)", [])
                if win_paths or steam_paths:
                    win = path_to_directory_only(str(win_paths[0]).strip()) if win_paths else None
                    steam = path_to_directory_only(str(steam_paths[0]).strip()) if steam_paths else None
                    return {'windows': win, 'steam': steam}
            return self._fallback_parse_html_section(page_title)
        except Exception as e:
            print(f"!! Error fetching for page '{page_title}': {e}")
            return None

    def fetch_save_location(self, game_name, steam_app_id):
        page_title = self._get_page_name_from_app_id(steam_app_id) if steam_app_id else None
        if page_title:
            print(f"--> Fetching semantic data from wiki page: '{page_title}'...")
            return self._fetch_for_page_title(page_title)
        clean_name = normalize_wiki_page_title(game_name)
        if clean_name != game_name:
            print(f"--> Using clean page title: '{game_name}' -> '{clean_name}'")
        # Try clean name first (so "Company of Heroes" works when wiki has that page)
        print(f"--> Fetching semantic data from wiki page: '{clean_name}'...")
        result = self._fetch_for_page_title(clean_name)
        if result:
            return result
        # No paths with clean name: search and pick closest match (e.g. "Lego Star Wars: The Complete Saga")
        page_title = self._get_page_name_from_search(clean_name)
        if page_title and page_title != clean_name:
            print(f"--> Fetching semantic data from wiki page: '{page_title}'...")
            result = self._fetch_for_page_title(page_title)
            if result:
                return result
        # Fallback: try direct title variants (colon, "Lego", slug) so we find pages like "Lego Star Wars: The Complete Saga"
        for variant in build_title_variants_for_fallback(clean_name):
            if variant == clean_name:
                continue
            print(f"--> Trying wiki title variant: '{variant}'...")
            result = self._fetch_for_page_title(variant)
            if result:
                return result
        return None