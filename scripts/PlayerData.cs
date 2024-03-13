using Godot;
using System.Text.Json;
using System.Threading.Tasks;

public partial class Player : CharacterBody2D
{
	public class SaveFile
	{
		public string Name { get; set; }
		public int Level { get; set; }
		public string Room { get; set; }

		public float PositionX { get; set; }
		public float PositionY { get; set; }

		public System.Collections.Generic.List<string> Inventory { get; set; }

		public SaveFile()
		{
			Inventory = new() { "aphid_egg" };
			Room = "resort_golden_grounds";
		}
	}

	public static SaveFile SAVE = new();
	public static SaveFileController SAVE_CONTROL = new();

	public class SaveFileController : SaveSystem.ISaveData
	{
		private const string playerData = "/player.data";
		public Task SaveData(string _path)
		{
			using var _file = FileAccess.Open(_path + playerData, FileAccess.ModeFlags.Write);
			var _jsonPlayer = JsonSerializer.Serialize(SAVE);
			_file.StorePascalString(_jsonPlayer);

			return Task.CompletedTask;
		}

		public Task LoadData(string _path)
		{
			using var _file = FileAccess.Open(_path + playerData, FileAccess.ModeFlags.Read);
			var _data = _file.GetPascalString();

			if (_data == string.Empty)
			{
				SAVE = new();
				GD.PrintErr("This player data was empty!");
				return Task.CompletedTask;
			}

			SAVE = JsonSerializer.Deserialize<SaveFile>(_data);
			return Task.CompletedTask;
		}
	}
}