//
//    MCSkin3D, a 3d skin management studio for Minecraft
//    Copyright (C) 2013 Altered Softworks & MCSkin3D Team
//
//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Linq;

namespace Paril.Settings
{
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
	public sealed class SavableAttribute : Attribute
	{
	}

	public interface ISavable
	{
		string SaveHeader { get; }
	}

	public interface ITypeSerializer
	{
		string Serialize(object obj);
		object Deserialize(string str);
	}

	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
	public class TypeSerializerAttribute : Attribute
	{
		public readonly bool DeserializeDefault;
		public readonly string TypeName;

		public TypeSerializerAttribute(Type type, bool deserializeDefault)
		{
			TypeName = type.FullName;
			DeserializeDefault = deserializeDefault;
		}
	}

	public class StringSerializer
	{
		public virtual string Serialize(object field, object obj)
		{
			try
			{
				if (field is PropertyInfo)
				{
					var info = (PropertyInfo)field;

					if (info.GetCustomAttributes(typeof(TypeSerializerAttribute), false).Length != 0)
					{
						var converter = (TypeSerializerAttribute)info.GetCustomAttributes(typeof(TypeSerializerAttribute), false)[0];
						Type type = Type.GetType(converter.TypeName);

						if (type != null)
						{
							var conv = (ITypeSerializer)(type.GetConstructors()[0].Invoke(null));
							return conv.Serialize(obj);
						}
					}
				}

				if (obj == null)
					return "";

				return TypeDescriptor.GetConverter(obj.GetType()).ConvertToString(obj);
			}
			catch (Exception ex)
			{
				if (field == null)
					throw new Exception("Wat, field is null");
				else
				{
					throw new Exception(
						"Failed to serialize member " + ((MemberInfo)field).Name + " [" + ((MemberInfo)field).MemberType.ToString() +
						"]", ex);
				}
			}
		}

		public virtual object Deserialize(object field, string str, Type t)
		{
			if (field is PropertyInfo)
			{
				var info = (PropertyInfo)field;

				if (info.GetCustomAttributes(typeof(TypeSerializerAttribute), false).Length != 0)
				{
					var converter = (TypeSerializerAttribute)info.GetCustomAttributes(typeof(TypeSerializerAttribute), false)[0];
					Type type = Type.GetType(converter.TypeName);

					if (type != null)
					{
						var conv = (ITypeSerializer)(type.GetConstructors()[0].Invoke(null));
						return conv.Deserialize(str);
					}
				}
			}

			return TypeDescriptor.GetConverter(t).ConvertFromString(str);
		}
	}

	public class StringArraySerializer : ITypeSerializer
	{
		#region ITypeSerializer Members

		public string Serialize(object obj)
		{
			var arr = (string[])obj;
			string combined = "";

			foreach (string c in arr)
			{
				if (combined == "")
					combined += c;
				else
					combined += ";" + c;
			}

			return combined;
		}

		public object Deserialize(string str)
		{
			return str.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		}

		#endregion
	}

	public class Settings
	{
		private readonly StringSerializer _serializer = new StringSerializer();
		public List<object> Structures = new List<object>();

		private Dictionary<string, Dictionary<string, PropertyInfo>> PropertiesBySection()
		{
			return Structures.OfType<Type>().ToDictionary(t => t.Name,
				t => t.GetProperties(BindingFlags.Static | BindingFlags.Public)
					.Where(p => p.IsDefined(typeof(SavableAttribute), false)).ToDictionary(p => p.Name));
		}

		public void Save(string fileName) => WriteSettings(fileName, false);

		private void WriteSettings(string fileName, bool addMissingOnly)
		{
			var sections = PropertiesBySection();
			var pending = sections.ToDictionary(s => s.Key, s => new HashSet<string>(s.Value.Keys));
			var lines = new List<string>();
			string section = null;
			bool changed = false;

			void AddMissingKeys()
			{
				if (section == null || !pending.TryGetValue(section, out var keys)) return;
				foreach (string key in keys)
				{
					var property = sections[section][key];
					lines.Add(key + "=" + _serializer.Serialize(property, property.GetValue(null)));
					changed = true;
				}
				keys.Clear();
			}

			// Preserve other sections, comments and unrecognized keys from earlier versions.
			if (File.Exists(fileName))
			{
				foreach (string original in File.ReadAllLines(fileName))
				{
					string line = original.Trim();
					if (line.StartsWith("[") && line.EndsWith("]"))
					{
						AddMissingKeys();
						section = line.Substring(1, line.Length - 2);
					}
					int equals = line.IndexOf('=');
					if (equals > 0 && section != null && sections.TryGetValue(section, out var properties))
					{
						string key = line.Substring(0, equals).Trim();
						if (properties.TryGetValue(key, out var property))
						{
							pending[section].Remove(key);
							if (!addMissingOnly)
							{
								lines.Add(key + "=" + _serializer.Serialize(property, property.GetValue(null)));
								continue;
							}
						}
					}
					lines.Add(original);
				}
			}
			AddMissingKeys();
			foreach (var pair in pending)
			{
				if (pair.Value.Count == 0) continue;
				section = pair.Key;
				lines.Add("[" + section + "]");
				AddMissingKeys();
			}
			if (addMissingOnly && !changed) return;
			string destination = Path.GetFullPath(fileName);
			string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
			try
			{
				using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				using (var output = new StreamWriter(stream))
				{
					foreach (string line in lines) output.WriteLine(line);
					output.Flush();
					stream.Flush(true);
				}
				File.Move(temporary, destination, true);
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}

		public void LoadDefaults()
		{
			foreach (object str in Structures)
			{
				if (str is Type)
				{
					var type = (Type)str;

					foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Static | BindingFlags.Public))
					{
						object[] attribs = prop.GetCustomAttributes(typeof(DefaultValueAttribute), false);
						if (attribs.Length != 0)
						{
							var dva = (DefaultValueAttribute)attribs[0];

							object[] converters = prop.GetCustomAttributes(typeof(TypeSerializerAttribute), false);

							if (converters.Length != 0)
							{
								var serialize = (TypeSerializerAttribute)converters[0];

								if (serialize.DeserializeDefault)
									prop.SetValue(null, _serializer.Deserialize(prop, dva.Value.ToString(), prop.PropertyType), null);
								else
									prop.SetValue(null, dva.Value, null);
							}
							else
								prop.SetValue(null, dva.Value, null);
						}
					}
				}
			}
		}

		public void Load(string fileName, bool upgradeMissingKeys = false)
		{
			LoadDefaults();
			if (!File.Exists(fileName))
			{
				Save(fileName);
				return;
			}

			var sections = PropertiesBySection();
			Dictionary<string, PropertyInfo> properties = null;
			foreach (string original in File.ReadAllLines(fileName))
			{
				string line = original.Trim();
				if (line.StartsWith("[") && line.EndsWith("]"))
				{
					sections.TryGetValue(line.Substring(1, line.Length - 2), out properties);
					continue;
				}
				int equals = line.IndexOf('=');
				if (equals <= 0 || properties == null || !properties.TryGetValue(line.Substring(0, equals).Trim(), out var property))
					continue;
				string value = line.Substring(equals + 1);
				try { property.SetValue(null, _serializer.Deserialize(property, value, property.PropertyType)); }
				catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is NotSupportedException ||
					ex is System.Security.Cryptography.CryptographicException || ex is OverflowException)
				{
					// A malformed preference keeps its default without discarding the rest of the file.
				}
			}
			if (upgradeMissingKeys) WriteSettings(fileName, true);
		}
	}
}
