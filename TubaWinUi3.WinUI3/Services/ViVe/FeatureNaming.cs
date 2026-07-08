using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TubaWinUi3.Services.ViVe;

public static class FeatureNaming
{
	public const string DictFileName = "FeatureDictionary.pfs";

	public static string DictFilePath => Path.Combine(AppContext.BaseDirectory, "FeatureDictionary.pfs");

	public static List<uint>? FindIdsForNames(IEnumerable<string> featureNames)
	{
		if (!File.Exists(DictFilePath))
		{
			return null;
		}
		List<uint> list = new List<uint>();
		List<string> list2 = featureNames.Select((string x) => x.ToLowerInvariant() + ",").ToList();
		using StreamReader streamReader = new StreamReader(File.OpenRead(DictFilePath));
		while (!streamReader.EndOfStream)
		{
			string? line = streamReader.ReadLine();
			if (string.IsNullOrEmpty(line))
			{
				continue;
			}
			string text = line.ToLowerInvariant();
			for (int num = list2.Count - 1; num >= 0; num--)
			{
				if (text.StartsWith(list2[num]))
				{
					if (uint.TryParse(text.Substring(list2[num].Length), out var id))
					{
						list.Add(id);
					}
					list2.RemoveAt(num);
					break;
				}
			}
			if (list2.Count == 0)
			{
				break;
			}
		}
		return list;
	}

	public static Dictionary<uint, string>? FindNamesForFeatures(IEnumerable<uint> featureIDs)
	{
		Dictionary<uint, string> dictionary = new Dictionary<uint, string>();
		if (!File.Exists(DictFilePath))
		{
			return null;
		}
		List<string> list = featureIDs.Select((uint x) => "," + x).ToList();
		using StreamReader streamReader = new StreamReader(File.OpenRead(DictFilePath));
		while (!streamReader.EndOfStream)
		{
			string? line = streamReader.ReadLine();
			if (string.IsNullOrEmpty(line) || !line.Contains(','))
			{
				continue;
			}
			for (int num = list.Count - 1; num >= 0; num--)
			{
				if (line.EndsWith(list[num]))
				{
					if (uint.TryParse(list[num].Substring(1), out var key))
					{
						dictionary[key] = line.Substring(0, line.IndexOf(','));
					}
					list.RemoveAt(num);
					break;
				}
			}
			if (list.Count == 0)
			{
				break;
			}
		}
		return dictionary;
	}
}
