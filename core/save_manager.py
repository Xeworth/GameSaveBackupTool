import os
import json
import re

from core.save_location_fetcher import path_to_directory_only


class SaveManager:
    def __init__(self, game_save_data_path="config/game_save_data.json"):
        self.game_save_data_path = game_save_data_path
        self.game_save_locations = self._load_game_save_data()

    def _load_game_save_data(self):
        """
        Loads the known game save locations from a JSON file.
        """
        if os.path.exists(self.game_save_data_path):
            try:
                with open(self.game_save_data_path, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except json.JSONDecodeError as e:
                print(f"Error decoding {self.game_save_data_path}: {e}. Returning empty data.")
                return {}
        return {}

    def _save_game_save_data(self):
        """
        Saves the current known game save locations to the JSON file.
        """
        # This line automatically creates the 'config' directory if it's missing
        os.makedirs(os.path.dirname(self.game_save_data_path), exist_ok=True)
        with open(self.game_save_data_path, 'w', encoding='utf-8') as f:
            json.dump(self.game_save_locations, f, indent=4)

    def add_or_update_save_location(self, game_name, data):
        """
        Adds or updates a game's save location data.
        """
        self.game_save_locations[game_name] = data
        self._save_game_save_data()

    def delete_games(self, game_names):
        """
        Removes a list of games from the saved data and updates the JSON file.
        """
        games_deleted = 0
        for name in game_names:
            if name in self.game_save_locations:
                del self.game_save_locations[name]
                games_deleted += 1
        
        if games_deleted > 0:
            self._save_game_save_data()   

    def update_last_backup(self, game_name, timestamp_str):
        """
        Updates the 'last_backup' timestamp for a specific game.
        """
        if game_name in self.game_save_locations:
            self.game_save_locations[game_name]['last_backup'] = timestamp_str
            self._save_game_save_data() 

    def get_save_location(self, game_name):
        """
        Retrieves the known save location dictionary for a given game.
        """
        return self.game_save_locations.get(game_name)

    def resolve_path(self, path, game_install_path=None):
        """
        Resolves environment variables and special placeholders in a given path.
        Normalizes to directory-only (up to last '\') so wiki filename/pattern
        suffixes (e.g. 'SaveGame*.LEGO Star Wars_SavedGame') are stripped.
        """
        if not path:
            return None

        path = path_to_directory_only(path)

        # Resolve standard environment variables (e.g., %APPDATA%, %USERPROFILE%)
        resolved_path = os.path.expandvars(path)

        # Resolve custom %INSTALLATION_PATH% placeholder
        if "%INSTALLATION_PATH%" in resolved_path and game_install_path:
            resolved_path = resolved_path.replace("%INSTALLATION_PATH%", game_install_path)

        if resolved_path.startswith('~'):
            resolved_path = os.path.expanduser(resolved_path)

        return resolved_path

    def backup_game_saves(self, game_name, source_path, destination_base_path):
        """
        Placeholder for backing up game saves.
        """
        print(f"Backing up saves for {game_name} from {source_path} to {destination_base_path} (placeholder)...")
        pass

    def merge_imported_games(self, imported: dict) -> int:
        """Merge ``imported`` name→entry map into ``game_save_locations``; returns count merged."""
        n = 0
        for name, data in imported.items():
            if not name or not isinstance(data, dict):
                continue
            self.game_save_locations[name] = data
            n += 1
        if n:
            self._save_game_save_data()
        return n

    def replace_all_games(self, imported: dict) -> None:
        """Replace catalog entirely with ``imported`` (must be name→dict)."""
        self.game_save_locations = {k: v for k, v in imported.items() if isinstance(v, dict)}
        self._save_game_save_data()