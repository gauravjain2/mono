//
// System.Type.cs
//
// Author:
//   Rodrigo Kumpera <kumpera@gmail.com>
//
//
// Copyright (C) 2010 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace System {
	internal interface ModifierSpec {
		Type Resolve (Type type);
		Text.StringBuilder Append (Text.StringBuilder sb);
	}
        internal class ArraySpec : ModifierSpec
	{
		// dimensions == 1 and bound, or dimensions > 1 and !bound
		int dimensions;
		bool bound;

		internal ArraySpec (int dimensions, bool bound)
		{
			this.dimensions = dimensions;
			this.bound = bound;
		}

		public Type Resolve (Type type)
		{
			if (bound)
				return type.MakeArrayType (1);
			else if (dimensions == 1)
				return type.MakeArrayType ();
			return type.MakeArrayType (dimensions);
		}

		public Text.StringBuilder Append (Text.StringBuilder sb)
		{
			if (bound)
				return sb.Append ("[*]");
			return sb.Append ('[')
				.Append (',', dimensions - 1)
				.Append (']');
			
		}
		public override string ToString ()
		{
			return Append (new Text.StringBuilder ()).ToString ();
		}

		public int Rank {
			get {
				return dimensions;
			}
		}

		public bool IsBound {
			get {
				return bound;
			}
		}
	}

	internal class PointerSpec : ModifierSpec
	{
		int pointer_level;

		internal PointerSpec (int pointer_level) {
			this.pointer_level = pointer_level;
		}

		public Type Resolve (Type type) {
			for (int i = 0; i < pointer_level; ++i)
				type = type.MakePointerType ();
			return type;
		}

		public Text.StringBuilder Append (Text.StringBuilder sb)
		{
			return sb.Append ('*', pointer_level);
		}
		
		public override string ToString () {
			return Append (new Text.StringBuilder ()).ToString ();
		}

	}

	internal class TypeSpec
	{
		TypeIdentifier name;
		string assembly_name;
		List<TypeIdentifier> nested;
		List<TypeSpec> generic_params;
		List<ModifierSpec> modifier_spec;
		bool is_byref;

		string display_fullname; // cache

		internal bool HasModifiers {
			get { return modifier_spec != null; }
		}

		internal bool IsNested {
			get { return nested != null && nested.Count > 0; }
		}

		internal bool IsByRef {
			get { return is_byref; }
		}

		internal TypeName Name {
			get { return name; }
		}

		internal IEnumerable<TypeName> Nested {
			get {
				if (nested != null)
					return nested;
				else
					return EmptyArray<TypeName>.Value;
			}
		}

		internal IEnumerable<ModifierSpec> Modifiers {
			get {
				if (modifier_spec != null)
					return modifier_spec;
				else
					return EmptyArray<ModifierSpec>.Value;
			}
		}

		[Flags]
		internal enum DisplayNameFormat {
			Default = 0x0,
			WANT_ASSEMBLY = 0x1,
			NO_MODIFIERS = 0x2,
		}
#if DEBUG
		public override string ToString () {
			return GetDisplayFullName (DisplayNameFormat.WANT_ASSEMBLY);
		}
#endif

		string GetDisplayFullName (DisplayNameFormat flags)
		{
			bool wantAssembly = (flags & DisplayNameFormat.WANT_ASSEMBLY) != 0;
			bool wantModifiers = (flags & DisplayNameFormat.NO_MODIFIERS) == 0;
			var sb = new Text.StringBuilder(name.DisplayName);
			if (nested != null) {
				foreach (var n in nested)
					sb.Append ('+').Append (n.DisplayName);
			}

			if (generic_params != null) {
				sb.Append ('[');
				for (int i = 0; i < generic_params.Count; ++i) {
					if (i > 0)
						sb.Append (", ");
					if (generic_params [i].assembly_name != null)
						sb.Append ('[').Append (generic_params [i].DisplayFullName).Append (']');
					else
						sb.Append (generic_params [i].DisplayFullName);
				}
				sb.Append (']');
			}

			if (wantModifiers)
				GetModifierString (sb);

			if (assembly_name != null && wantAssembly)
				sb.Append (", ").Append (assembly_name);

			return sb.ToString();
		}

		internal string ModifierString ()
		{
			return GetModifierString (new Text.StringBuilder ()).ToString ();
		}

		private Text.StringBuilder GetModifierString (Text.StringBuilder sb)
		{
			if (modifier_spec != null) {
				foreach (var md in modifier_spec)
					md.Append (sb);
			}

			if (is_byref)
				sb.Append ('&');

			return sb;
		}

		internal string DisplayFullName {
			get {
				if (display_fullname == null)
					display_fullname = GetDisplayFullName (DisplayNameFormat.Default);
				return display_fullname;
			}
		}

		internal static TypeSpec Parse (string typeName)
		{
			int pos = 0;
			if (typeName == null)
				throw new ArgumentNullException ("typeName");

			TypeSpec res = Parse (typeName, ref pos, false, true);
			if (pos < typeName.Length)
				throw new ArgumentException ("Count not parse the whole type name", "typeName");
			return res;
		}

		internal static string EscapeDisplayName(string internalName)
		{
			// initial capacity = length of internalName.
			// Maybe we won't have to escape anything.
			var res = new Text.StringBuilder (internalName.Length);
			foreach (char c in internalName)
			{
				switch (c) {
					case '+':
					case ',':
					case '[':
					case ']':
					case '*':
					case '&':
					case '\\':
						res.Append ('\\').Append (c);
						break;
					default:
						res.Append (c);
						break;
				}
			}
			return res.ToString ();
		}

		internal static string UnescapeInternalName(string displayName)
		{
			var res = new Text.StringBuilder (displayName.Length);
			for (int i = 0; i < displayName.Length; ++i)
			{
				char c = displayName[i];
				if (c == '\\')
					if (++i < displayName.Length)
						c = displayName[i];
				res.Append (c);
			}
			return res.ToString ();
		}

		internal static bool NeedsEscaping (string internalName)
		{
			foreach (char c in internalName)
			{
				switch (c) {
					case ',':
					case '+':
					case '*':
					case '&':
					case '[':
					case ']':
					case '\\':
						return true;
					default:
						break;
				}
			}
			return false;
		}

		internal Type Resolve (Func<AssemblyName,Assembly> assemblyResolver, Func<Assembly,string,bool,Type> typeResolver, bool throwOnError, bool ignoreCase)
		{
			Assembly asm = null;
			if (assemblyResolver == null && typeResolver == null)
				return Type.GetType (DisplayFullName, throwOnError, ignoreCase);

			if (assembly_name != null) {
				if (assemblyResolver != null)
					asm = assemblyResolver (new AssemblyName (assembly_name));
				else
					asm = Assembly.Load (assembly_name);

				if (asm == null) {
					if (throwOnError)
						throw new FileNotFoundException ("Could not resolve assembly '" + assembly_name + "'");
					return null;
				}
			}

			Type type = null;
			if (typeResolver != null)
				type = typeResolver (asm, name.DisplayName, ignoreCase);
			else
				type = asm.GetType (name.DisplayName, false, ignoreCase);
			if (type == null) {
				if (throwOnError)
					throw new TypeLoadException ("Could not resolve type '" + name + "'");
				return null;
			}

			if (nested != null) {
				foreach (var n in nested) {
					var tmp = type.GetNestedType (n.DisplayName, BindingFlags.Public | BindingFlags.NonPublic);
					if (tmp == null) {
						if (throwOnError)
							throw new TypeLoadException ("Could not resolve type '" + n + "'");
						return null;
					}
					type = tmp;
				}
			}

			if (generic_params != null) {
				Type[] args = new Type [generic_params.Count];
				for (int i = 0; i < args.Length; ++i) {
					var tmp = generic_params [i].Resolve (assemblyResolver, typeResolver, throwOnError, ignoreCase);
					if (tmp == null) {
						if (throwOnError)
							throw new TypeLoadException ("Could not resolve type '" + generic_params [i].name + "'");
						return null;
					}
					args [i] = tmp;
				}
				type = type.MakeGenericType (args);
			}

			if (modifier_spec != null) {
				foreach (var md in modifier_spec)
					type = md.Resolve (type);
			}

			if (is_byref)
				type = type.MakeByRefType ();

			return type;
		}

		void AddName (string type_name)
		{
			if (name == null) {
				name = ParsedTypeIdentifier(type_name);
			} else {
				if (nested == null)
					nested = new List <TypeIdentifier> ();
				nested.Add (ParsedTypeIdentifier(type_name));
			}
		}

		void AddModifier (ModifierSpec md)
		{
			if (modifier_spec == null)
				modifier_spec = new List<ModifierSpec> ();
			modifier_spec.Add (md);
		}

		static void SkipSpace (string name, ref int pos)
		{
			int p = pos;
			while (p < name.Length && Char.IsWhiteSpace (name [p]))
				++p;
			pos = p;
		}

		static void BoundCheck (int idx, string s)
		{
			if (idx >= s.Length)
				throw new ArgumentException ("Invalid generic arguments spec", "typeName");
		}

		static TypeIdentifier ParsedTypeIdentifier (string displayName)
		{
			return TypeIdentifiers.FromDisplay(displayName);
		}

		static TypeSpec Parse (string name, ref int p, bool is_recurse, bool allow_aqn)
		{
			// Invariants:
			//  - On exit p, is updated to pos the current unconsumed character.
			//
			//  - The callee peeks at but does not consume delimiters following
			//    recurisve parse (so for a recursive call like the args of "Foo[P,Q]"
			//    we'll return with p either on ',' or on ']'.  If the name was aqn'd
			//    "Foo[[P,assmblystuff],Q]" on return p with be on the ']' just
			//    after the "assmblystuff")
			//
			//  - If allow_aqn is True, assembly qualification is optional.
			//    If allow_aqn is False, assembly qualification is prohibited.
			int pos = p;
			int name_start;
			bool in_modifiers = false;
			TypeSpec data = new TypeSpec ();

			SkipSpace (name, ref pos);

			name_start = pos;

			for (; pos < name.Length; ++pos) {
				switch (name [pos]) {
				case '+':
					data.AddName (name.Substring (name_start, pos - name_start));
					name_start = pos + 1;
					break;
				case ',':
				case ']':
					data.AddName (name.Substring (name_start, pos - name_start));
					name_start = pos + 1;
					in_modifiers = true;
					if (is_recurse && !allow_aqn) {
						p = pos;
						return data;
					}
					break;
				case '&':
				case '*':
				case '[':
					if (name [pos] != '[' && is_recurse)
						throw new ArgumentException ("Generic argument can't be byref or pointer type", "typeName");
					data.AddName (name.Substring (name_start, pos - name_start));
					name_start = pos + 1;
					in_modifiers = true;
					break;
				case '\\':
					pos++;
					break;
				}
				if (in_modifiers)
					break;
			}

			if (name_start < pos)
				data.AddName (name.Substring (name_start, pos - name_start));
			else if (name_start == pos)
				data.AddName (String.Empty);

			if (in_modifiers) {
				for (; pos < name.Length; ++pos) {

					switch (name [pos]) {
					case '&':
						if (data.is_byref)
							throw new ArgumentException ("Can't have a byref of a byref", "typeName");

						data.is_byref = true;
						break;
					case '*':
						if (data.is_byref)
							throw new ArgumentException ("Can't have a pointer to a byref type", "typeName");
						// take subsequent '*'s too
						int pointer_level = 1;
						while (pos+1 < name.Length && name[pos+1] == '*') {
							++pos;
							++pointer_level;
						}
						data.AddModifier (new PointerSpec(pointer_level));
						break;
					case ',':
						if (is_recurse && allow_aqn) {
							int end = pos;
							while (end < name.Length && name [end] != ']')
								++end;
							if (end >= name.Length)
								throw new ArgumentException ("Unmatched ']' while parsing generic argument assembly name");
							data.assembly_name = name.Substring (pos + 1, end - pos - 1).Trim ();
							p = end;
							return data;						
						}
						if (is_recurse) {
							p = pos;
							return data;
						}
						if (allow_aqn) {
							data.assembly_name = name.Substring (pos + 1).Trim ();
							pos = name.Length;
						}
						break;
					case '[':
						if (data.is_byref)
							throw new ArgumentException ("Byref qualifier must be the last one of a type", "typeName");
						++pos;
						if (pos >= name.Length)
								throw new ArgumentException ("Invalid array/generic spec", "typeName");
						SkipSpace (name, ref pos);

						if (name [pos] != ',' && name [pos] != '*' && name [pos]  != ']') {//generic args
							List<TypeSpec> args = new List <TypeSpec> ();
							if (data.HasModifiers)
								throw new ArgumentException ("generic args after array spec or pointer type", "typeName");

							while (pos < name.Length) {
								SkipSpace (name, ref pos);
								bool aqn = name [pos] == '[';
								if (aqn)
									++pos; //skip '[' to the start of the type
								args.Add (Parse (name, ref pos, true, aqn));
								BoundCheck (pos, name);
								if (aqn) {
									if (name [pos] == ']')
										++pos;
									else
										throw new ArgumentException ("Unclosed assembly-qualified type name at " + name[pos], "typeName");
									BoundCheck (pos, name);
}

								if (name [pos] == ']')
									break;
								if (name [pos] == ',')
									++pos; // skip ',' to the start of the next arg
								else
									throw new ArgumentException ("Invalid generic arguments separator " + name [pos], "typeName");
			
							}
							if (pos >= name.Length || name [pos] != ']')
								throw new ArgumentException ("Error parsing generic params spec", "typeName");
							data.generic_params = args;
						} else { //array spec
							int dimensions = 1;
							bool bound = false;
							while (pos < name.Length && name [pos] != ']') {
								if (name [pos] == '*') {
									if (bound)
										throw new ArgumentException ("Array spec cannot have 2 bound dimensions", "typeName");
									bound = true;
								}
								else if (name [pos] != ',')
									throw new ArgumentException ("Invalid character in array spec " + name [pos], "typeName");
								else
									++dimensions;

								++pos;
								SkipSpace (name, ref pos);
							}
							if (pos >= name.Length || name [pos] != ']')
								throw new ArgumentException ("Error parsing array spec", "typeName");
							if (dimensions > 1 && bound)
								throw new ArgumentException ("Invalid array spec, multi-dimensional array cannot be bound", "typeName");
							data.AddModifier (new ArraySpec (dimensions, bound));
						}

						break;
					case ']':
						if (is_recurse) {
							p = pos;
							return data;
						}
						throw new ArgumentException ("Unmatched ']'", "typeName");
					default:
						throw new ArgumentException ("Bad type def, can't handle '" + name [pos]+"'" + " at " + pos, "typeName");
					}
				}
			}

			p = pos;
			return data;
		}

		internal TypeName TypeNameWithoutModifiers ()
		{
			return new TypeSpecTypeName (this, false);
		}
		
		internal TypeName TypeName {
			get { return new TypeSpecTypeName (this, true); }
		}

		private class TypeSpecTypeName : TypeNames.ATypeName, TypeName {
			TypeSpec ts;
			bool want_modifiers;

			internal TypeSpecTypeName (TypeSpec ts, bool wantModifiers)
			{
				this.ts = ts;
				this.want_modifiers = wantModifiers;
			}

			public override string DisplayName {
				get {
					if (want_modifiers)
						return ts.DisplayFullName;
					else
						return ts.GetDisplayFullName (DisplayNameFormat.NO_MODIFIERS);
				}
			}

			public override TypeName NestedName (TypeIdentifier innerName)
			{
				return TypeNames.FromDisplay(DisplayName + "+" + innerName.DisplayName);
			}
		}

	}
}

