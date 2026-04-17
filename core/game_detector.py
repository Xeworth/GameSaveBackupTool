import os
import winreg # For interacting with Windows Registry
import re     # For regular expressions, useful for parsing .acf files
import sqlite3  # For GOG Galaxy database

class GameDetector:
    def __init__(self):
        self._steam_common_roots_cache: frozenset[str] | None = None

    def _get_steam_install_path(self):
        """
        Attempts to find the Steam installation path from the Windows Registry.
        """
        try:
            # Open the registry key where Steam's install path is typically stored
            key_path = r"SOFTWARE\Wow6432Node\Valve\Steam" # Common for 64-bit Windows
            reg_key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, key_path)
            install_path, _ = winreg.QueryValueEx(reg_key, "InstallPath")
            winreg.CloseKey(reg_key)
            return install_path
        except FileNotFoundError:
            # Try 32-bit path if 64-bit fails
            try:
                key_path = r"SOFTWARE\Valve\Steam"
                reg_key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, key_path)
                install_path, _ = winreg.QueryValueEx(reg_key, "InstallPath")
                winreg.CloseKey(reg_key)
                return install_path
            except FileNotFoundError:
                print("Steam installation not found in Registry.")
                return None
        except Exception as e:
            print(f"Error getting Steam install path from Registry: {e}")
            return None

    def _parse_vdf_file(self, file_path):
        """
        Parses a Valve Data Format (VDF) file for appmanifests.
        """
        data = {}
        if not os.path.exists(file_path):
            return data

        with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()

        # This regex looks for "key" "value" patterns
        matches = re.findall(r'"([^"]+)"\s+"([^"]+)"', content)
        for key, value in matches:
            # Add .strip() to clean up the value from extra whitespace
            data[key] = value.strip()
        return data

    def detect_steam_games(self):
        """
        Detects installed Steam games by reading registry and appmanifest files.
        """
        steam_path = self._get_steam_install_path()
        if not steam_path:
            return []

        steam_apps_path = os.path.join(steam_path, "steamapps")
        if not os.path.exists(steam_apps_path):
            return []

        library_folders_path = os.path.join(steam_apps_path, "libraryfolders.vdf")
        
        library_paths = [steam_apps_path]
        if os.path.exists(library_folders_path):
            vdf_data = self._parse_vdf_file(library_folders_path)
            for key, value in vdf_data.items():
                if key.isdigit() and os.path.isdir(os.path.join(value, "steamapps")):
                    library_paths.append(os.path.join(value, "steamapps"))

        found_games = []
        for lib_path in set(library_paths):
            if not os.path.exists(lib_path):
                continue

            for item in os.listdir(lib_path):
                if item.startswith("appmanifest_") and item.endswith(".acf"):
                    acf_path = os.path.join(lib_path, item)
                    try:
                        acf_data = self._parse_vdf_file(acf_path)
                        
                        app_id = acf_data.get("appid")
                        game_name = acf_data.get("name")
                        install_dir = acf_data.get("installdir")
                        
                        if app_id and game_name and install_dir and acf_data.get("StateFlags") == "4":
                            game_full_path = os.path.join(lib_path, "common", install_dir)
                            if os.path.exists(game_full_path):
                                found_games.append({
                                    "name": game_name,
                                    "app_id": app_id,
                                    "install_path": game_full_path,
                                    "platform": "Steam"
                                })
                    except Exception as e:
                        print(f"Error parsing appmanifest file {acf_path}: {e}")
        
        return found_games

    def _get_steam_library_common_roots(self) -> frozenset[str]:
        """
        Normalized lowercase paths to each Steam library's steamapps\\common folder.
        Used to trust Uninstall entries that live under a real Steam game tree without
        relying on publisher name (indie / unknown publisher).
        """
        if self._steam_common_roots_cache is not None:
            return self._steam_common_roots_cache
        roots: list[str] = []
        steam_path = self._get_steam_install_path()
        if not steam_path:
            self._steam_common_roots_cache = frozenset()
            return self._steam_common_roots_cache
        steam_apps_path = os.path.join(steam_path, "steamapps")
        if not os.path.isdir(steam_apps_path):
            self._steam_common_roots_cache = frozenset()
            return self._steam_common_roots_cache
        library_paths = [steam_apps_path]
        library_folders_path = os.path.join(steam_apps_path, "libraryfolders.vdf")
        if os.path.exists(library_folders_path):
            vdf_data = self._parse_vdf_file(library_folders_path)
            for key, value in vdf_data.items():
                if key.isdigit():
                    p = os.path.join(value, "steamapps")
                    if os.path.isdir(p):
                        library_paths.append(p)
        for lib_path in set(library_paths):
            common = os.path.join(lib_path, "common")
            if os.path.isdir(common):
                roots.append(os.path.normpath(common).lower())
        self._steam_common_roots_cache = frozenset(roots)
        return self._steam_common_roots_cache

    def _install_under_steam_common(self, install_location: str) -> bool:
        if not install_location or not os.path.isdir(install_location):
            return False
        n = os.path.normpath(install_location).lower()
        for root in self._get_steam_library_common_roots():
            prefix = root + os.sep
            if n == root or n.startswith(prefix):
                return True
        return False

    def _steam_common_junk_path(self, install_lower: str) -> bool:
        """Tools and redistributables under steamapps\\common — not playable games."""
        junk = (
            r"\steamworks common",
            "steamworks common redistributable",
            "\\tools\\",
            "\\directx\\",
            "\\_commonredist",
            "dedicated server",
            "redistributables",
            "vc_redist",
            "dotnet",
            "openxr",
            "epic online services",
        )
        return any(j in install_lower for j in junk)

    def _install_has_steam_appid_marker(self, install_location: str) -> bool:
        """True if install folder (or its parent) contains steam_appid.txt — strong Steam-game signal."""
        if not install_location or not os.path.isdir(install_location):
            return False
        for base in (install_location, os.path.dirname(os.path.normpath(install_location))):
            if base and os.path.isfile(os.path.join(base, "steam_appid.txt")):
                return True
        return False

    def _name_or_publisher_excluded(self, display_name: str, publisher: str) -> bool:
        name_lower = (display_name or "").lower()
        publisher_lower = (publisher or "").lower()
        exclude_keywords = [
            "microsoft visual c++",
            "microsoft .net",
            "windows sdk",
            "directx",
            "update for",
            "security update",
            "hotfix",
            "kb",
            "runtime",
            "redistributable",
            "driver",
            "nvidia",
            "amd",
            "intel",
            "adobe",
            "java",
            "python",
            "node.js",
            "git",
            "visual studio",
            "microsoft office",
            "windows",
            "system",
            "discord",
            "slack",
            "teams",
            "zoom",
            "skype",
            "telegram",
            "whatsapp",
            "spotify",
            "itunes",
            "chrome",
            "firefox",
            "edge",
            "brave",
            "opera",
            "browser",
            "vscode",
            "code editor",
            "sublime",
            "notepad++",
            "7-zip",
            "winrar",
            "obs",
            "streamlabs",
            "launcher",
            "geforce experience",
            "radeon software",
            "corsair",
            "logitech",
            "razer",
            "steelseries",
            "steamworks common redistributables",
            "steam client bootstrapper",
            "steam setup",
            "gog galaxy",
            "riot games",
            "riot client",
            "vlc media",
            "vlc ",
            "cpu-z",
            "hwinfo",
            "hwmonitor",
            "malwarebytes",
            "ccleaner",
            "audacity",
            "gimp ",
            "blender",
            "paint.net",
            "sharex",
            "everything",
            "treesize",
            "wireshark",
        ]
        for keyword in exclude_keywords:
            if keyword in name_lower or keyword in publisher_lower:
                return True
        return False

    def _detect_platform_from_registry_entry(self, display_name, publisher, install_location):
        """
        Detects platform from registry entry data using heuristics.
        Returns platform string or None.
        """
        if not publisher:
            publisher = ""
        if not install_location:
            install_location = ""
        if not display_name:
            display_name = ""
        
        publisher_lower = publisher.lower()
        install_lower = install_location.lower()
        name_lower = display_name.lower()
        
        # GOG detection
        if "gog" in publisher_lower or "gog.com" in publisher_lower:
            return "GOG"
        if "gog" in install_lower or "galaxy" in install_lower:
            return "GOG"
        
        # Epic Games Store
        if "epic" in publisher_lower or "epic games" in publisher_lower:
            return "Epic"
        if "epic" in install_lower:
            return "Epic"
        
        # EA / Origin
        if "electronic arts" in publisher_lower or "ea " in publisher_lower or publisher_lower == "ea":
            return "EA"
        if "origin" in install_lower:
            return "EA"
        
        # Ubisoft / Uplay
        if "ubisoft" in publisher_lower:
            return "Ubisoft"
        if "uplay" in install_lower:
            return "Ubisoft"
        
        # Steam (though we prefer appmanifest detection)
        if "valve" in publisher_lower or "steam" in publisher_lower:
            return "Steam"
        if "steamapps" in install_lower or "steam" in install_lower:
            return "Steam"
        
        # Xbox / Microsoft Store
        if "microsoft" in publisher_lower and "windowsapps" in install_lower:
            return "Xbox"
        if "xbox" in publisher_lower:
            return "Xbox"
        
        # Battle.net / Blizzard
        if "blizzard" in publisher_lower or "battle.net" in publisher_lower:
            return "Battle.net"
        
        # Rockstar
        if "rockstar" in publisher_lower:
            return "Rockstar"
        
        # Bethesda
        if "bethesda" in publisher_lower:
            return "Bethesda"
        
        # Default: PC (for games without clear platform indicator)
        return "PC"

    def _path_suggests_non_game_software(self, install_lower: str) -> bool:
        """Hard path heuristics: well-known app roots that sometimes have Uninstall entries."""
        bad_roots = (
            "\\bravesoftware\\",
            "\\mozilla firefox",
            "\\google\\chrome",
            "\\microsoft\\edge",
            "\\7-zip",
            "\\videolan\\vlc",
            "\\notepad++",
            "\\cursor\\",
            "\\docker\\",
            "\\nodejs\\",
            "\\python",
            "\\jetbrains\\",
            "\\slack\\",
            "\\teams installer",
            "\\discord\\",
            "\\obs-studio",
            "\\sharex",
            "\\vlc",
        )
        return any(b in install_lower for b in bad_roots)

    def _is_likely_game(self, display_name, publisher, install_location):
        """
        Heuristic to determine if a registry Uninstall entry is likely a game.

        Tier A — strict: known store / publisher strings or typical game install folders.
        Tier B — Steam tree: install dir under steamapps\\common (any library) with junk
                 paths excluded; catches indies whose publisher string is unknown.
        Tier C — steam_appid.txt next to the game exe tree (strong Steam signal).
        """
        if not display_name or not install_location:
            return False
        
        # Must have an install location that exists
        if not os.path.exists(install_location):
            return False
        
        name_lower = display_name.lower()
        publisher_lower = (publisher or "").lower()
        install_lower = install_location.lower()

        if self._name_or_publisher_excluded(display_name, publisher):
            return False

        if self._path_suggests_non_game_software(install_lower):
            return False

        # Under Steam's common folder → treat as game unless path/name looks like junk
        if self._install_under_steam_common(install_location):
            if self._steam_common_junk_path(install_lower):
                return False
            if "redistributable" in name_lower or "redistributable" in publisher_lower:
                return False
            return True

        if self._install_has_steam_appid_marker(install_location):
            if self._steam_common_junk_path(install_lower):
                return False
            return True
        
        # Include ONLY if it's from a known game publisher/platform (strict)
        game_indicators = [
            "gog",
            "epic",
            "steam",
            "ubisoft",
            "electronic arts",
            "ea ",
            "blizzard",
            "bethesda",
            "rockstar",
            "activision",
            "square enix",
            "capcom",
            "bandai",
            "warner bros",
            "2k games",
            "paradox",
            "humble",
            "itch.io",
            "focus entertainment",
            "thq",
            "sega",
            "konami",
            "namco",
            "atlus",
            "nintendo",
            "xbox game studios",
        ]
        
        for indicator in game_indicators:
            if indicator in publisher_lower or indicator in install_lower:
                return True
        
        # Include if install path contains common game folder names
        game_folders = ["games", "game", "steamapps", "gog", "epic", "origin", "uplay", "xboxgames"]
        for folder in game_folders:
            if folder in install_lower:
                return True
        
        # Exclude if it's clearly not a game (too generic)
        if len(display_name) < 3:
            return False
        
        # Default: EXCLUDE - only include items we're confident are games
        # (user can add custom games manually if something is missed)
        return False

    def detect_registry_games(self):
        """
        Detects installed games from Windows Registry Uninstall keys.
        Scans HKLM and HKCU for programs that appear to be games.
        """
        found_games = []
        registry_keys = [
            (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        ]
        
        for hkey, key_path in registry_keys:
            try:
                reg_key = winreg.OpenKey(hkey, key_path)
                i = 0
                while True:
                    try:
                        subkey_name = winreg.EnumKey(reg_key, i)
                        subkey_path = os.path.join(key_path, subkey_name)
                        subkey = winreg.OpenKey(hkey, subkey_path)
                        
                        try:
                            display_name = winreg.QueryValueEx(subkey, "DisplayName")[0]
                        except FileNotFoundError:
                            display_name = None
                        
                        try:
                            publisher = winreg.QueryValueEx(subkey, "Publisher")[0]
                        except FileNotFoundError:
                            publisher = None
                        
                        try:
                            install_location = winreg.QueryValueEx(subkey, "InstallLocation")[0]
                        except FileNotFoundError:
                            install_location = None
                        
                        winreg.CloseKey(subkey)
                        
                        # Check if this looks like a game
                        if self._is_likely_game(display_name, publisher, install_location):
                            platform = self._detect_platform_from_registry_entry(
                                display_name, publisher, install_location
                            )
                            
                            found_games.append({
                                "name": display_name,
                                "app_id": None,  # Registry entries don't have app IDs
                                "install_path": install_location,
                                "platform": platform or "PC"
                            })
                        
                        i += 1
                    except OSError:
                        break
                
                winreg.CloseKey(reg_key)
            except FileNotFoundError:
                continue
            except Exception as e:
                print(f"Error scanning registry key {key_path}: {e}")
                continue
        
        return found_games

    def detect_gog_games(self):
        """
        Attempts to detect GOG games from GOG Galaxy database.
        Falls back to registry detection if database is not available.
        """
        gog_games = []
        
        # Try common GOG Galaxy database locations
        possible_db_paths = [
            r"C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db",
            os.path.join(os.environ.get("LOCALAPPDATA", ""), r"GOG.com\Galaxy\storage\galaxy-2.0.db"),
        ]
        
        db_path = None
        for path in possible_db_paths:
            if os.path.exists(path):
                db_path = path
                break
        
        if db_path:
            try:
                conn = sqlite3.connect(db_path)
                cursor = conn.cursor()
                
                # Try to find installed games - GOG Galaxy might store this in various tables
                # Common table names: InstalledBasePath, InstalledGames, GamePieces
                try:
                    # Try InstalledBasePath table (if it exists)
                    cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name LIKE '%install%'")
                    install_tables = cursor.fetchall()
                    
                    # Try common queries for installed games
                    # This is experimental - GOG Galaxy schema may vary
                    for table_row in install_tables:
                        table_name = table_row[0]
                        try:
                            # Try to get releaseKey and path
                            cursor.execute(f"SELECT releaseKey, basePath FROM {table_name} WHERE basePath IS NOT NULL")
                            results = cursor.fetchall()
                            for release_key, base_path in results:
                                if base_path and os.path.exists(base_path):
                                    # Try to get game title from GamePieces
                                    try:
                                        cursor.execute(
                                            "SELECT value FROM GamePieces WHERE releaseKey=? AND gamePieceTypeId=(SELECT id FROM GamePieceTypes WHERE type='title') LIMIT 1",
                                            (release_key,)
                                        )
                                        title_result = cursor.fetchone()
                                        game_name = title_result[0] if title_result else release_key
                                        
                                        # Parse JSON if needed
                                        if isinstance(game_name, str) and game_name.startswith('{'):
                                            import json
                                            try:
                                                title_data = json.loads(game_name)
                                                game_name = title_data.get('title', release_key)
                                            except:
                                                pass
                                        
                                        gog_games.append({
                                            "name": game_name,
                                            "app_id": None,
                                            "install_path": base_path,
                                            "platform": "GOG"
                                        })
                                    except:
                                        # If we can't get title, use release key
                                        gog_games.append({
                                            "name": release_key,
                                            "app_id": None,
                                            "install_path": base_path,
                                            "platform": "GOG"
                                        })
                        except:
                            continue
                except Exception as e:
                    print(f"Error querying GOG Galaxy database: {e}")
                
                conn.close()
            except Exception as e:
                print(f"Error accessing GOG Galaxy database at {db_path}: {e}")
        
        # Fallback: use registry detection for GOG games
        if not gog_games:
            registry_games = self.detect_registry_games()
            gog_games = [g for g in registry_games if g.get("platform") == "GOG"]
        
        return gog_games

    def detect_epic_games(self):
        """
        Detects Epic Games Store games from registry.
        Epic games are typically registered in Uninstall keys.
        """
        registry_games = self.detect_registry_games()
        return [g for g in registry_games if g.get("platform") == "Epic"]

    def detect_all_games(self):
        """
        Detects all installed games from various sources.
        Deduplicates Steam games (prefers appmanifest data over registry).
        """
        all_games = []
        seen_paths = set()  # For deduplication
        
        # Steam games (from appmanifests - most accurate)
        steam_games = self.detect_steam_games()
        for game in steam_games:
            all_games.append(game)
            seen_paths.add(os.path.normpath(game["install_path"]).lower())
        
        # GOG games (try database first, fallback to registry)
        gog_games = self.detect_gog_games()
        for game in gog_games:
            normalized_path = os.path.normpath(game["install_path"]).lower()
            if normalized_path not in seen_paths:
                all_games.append(game)
                seen_paths.add(normalized_path)
        
        # Epic games
        epic_games = self.detect_epic_games()
        for game in epic_games:
            normalized_path = os.path.normpath(game["install_path"]).lower()
            if normalized_path not in seen_paths:
                all_games.append(game)
                seen_paths.add(normalized_path)
        
        # All other registry games (excluding Steam/GOG/Epic already found)
        registry_games = self.detect_registry_games()
        for game in registry_games:
            normalized_path = os.path.normpath(game["install_path"]).lower()
            platform = game.get("platform", "PC")
            
            # Skip if already found via platform-specific detection
            if platform == "Steam" or platform == "GOG" or platform == "Epic":
                continue
            
            if normalized_path not in seen_paths:
                all_games.append(game)
                seen_paths.add(normalized_path)
        
        return all_games