//
// rootcontext.cs: keeps track of our tree representation, and assemblies loaded.
//
// Author: Miguel de Icaza (miguel@ximian.com)
//
// Licensed under the terms of the GNU GPL
//
// (C) 2001 Ximian, Inc (http://www.ximian.com)

using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;

namespace Mono.CSharp {

	public class RootContext {

		//
		// Contains the parsed tree
		//
		static Tree tree;

		//
		// Contains loaded assemblies and our generated code as we go.
		//
		static public TypeManager TypeManager;

		//
		// The System.Reflection.Emit CodeGenerator
		//
		static CodeGen cg;

		static public bool Optimize;
		
		//
		// The module builder pointer.
		//
		static ModuleBuilder mb;

		//
		// Whether we are being linked against the standard libraries.
		// This is only used to tell whether `System.Object' should
		// have a parent or not.
		//
		static bool stdlib = true;

		//
		// This keeps track of the order in which classes were defined
		// so that we can poulate them in that order.
		//
		// Order is important, because we need to be able to tell by
		// examining the parent's list of methods which ones are virtual
		// or abstract as well as the parent names (to implement new, 
		// override).
		//
		static ArrayList type_container_resolve_order;
		static ArrayList interface_resolve_order;

		//
		// Holds a reference to the Private Implementation Details
		// class.
		//
		static TypeBuilder impl_details_class;

		public static int WarningLevel = 2;
		
		//
		// Constructor
		//
		static RootContext ()
		{
			tree = new Tree ();
			TypeManager = new TypeManager ();
		}

		static public Tree Tree {
			get {
				return tree;
			}
		}

		static public string MainClass;
		
		static public CodeGen CodeGen {
			get {
				return cg;
			}

			set {
				//
				// Temporary hack, we should probably
				// intialize `cg' rather than depending on
				// external initialization of it.
				//
				cg = value;
				mb = cg.ModuleBuilder;
			}
		}

		public static void RegisterOrder (Interface iface)
		{
			interface_resolve_order.Add (iface);
		}
		
		public static void RegisterOrder (TypeContainer tc)
		{
			type_container_resolve_order.Add (tc);
		}
		
		// 
		// The default compiler checked state
		//
		static public bool Checked = false;

		//
		// Whether to allow Unsafe code
		//
		static public bool Unsafe = false;
		
		static string MakeFQN (string nsn, string name)
		{
			string prefix = (nsn == "" ? "" : nsn + ".");

			return prefix + name;
		}
		       
		// <remarks>
		//   This function is used to resolve the hierarchy tree.
		//   It processes interfaces, structs and classes in that order.
		//
		//   It creates the TypeBuilder's as it processes the user defined
		//   types.  
		// </remarks>
		static public void ResolveTree ()
		{
			//
			// Interfaces are processed first, as classes and
			// structs might inherit from an object or implement
			// a set of interfaces, we need to be able to tell
			// them appart by just using the TypeManager.
			//

			TypeContainer root = Tree.Types;

			ArrayList ifaces = root.Interfaces;
			if (ifaces != null){
				interface_resolve_order = new ArrayList ();
				
				foreach (Interface i in ifaces) 
					i.DefineInterface (mb);
			}
						
			type_container_resolve_order = new ArrayList ();
			
			foreach (TypeContainer tc in root.Types) 
				tc.DefineType (mb);

			if (root.Delegates != null)
				foreach (Delegate d in root.Delegates) 
					d.DefineDelegate (mb);

			if (root.Enums != null)
				foreach (Enum e in root.Enums)
					e.DefineEnum (mb);
			
		}
			
		// <summary>
		//   Closes all open types
		// </summary>
		//
		// <remarks>
		//   We usually use TypeBuilder types.  When we are done
		//   creating the type (which will happen after we have added
		//   methods, fields, etc) we need to "Define" them before we
		//   can save the Assembly
		// </remarks>
		static public void CloseTypes ()
		{
			TypeContainer root = Tree.Types;
			
			ArrayList ifaces = root.Interfaces;

			if (ifaces != null)
				foreach (Interface i in ifaces) 
					i.CloseType ();
			
			foreach (TypeContainer tc in root.Types)
				tc.CloseType ();

			if (root.Delegates != null)
				foreach (Delegate d in root.Delegates)
					d.CloseDelegate ();

			if (root.Enums != null)
				foreach (Enum en in root.Enums)
					en.CloseEnum ();
			

			//
			// If we have a <PrivateImplementationDetails> class, close it
			//
			if (impl_details_class != null){
				impl_details_class.CreateType ();
			}
		}
		
		//
		// Public function used to locate types, this can only
		// be used after the ResolveTree function has been invoked.
		//
		// Returns: Type or null if they type can not be found.
		//
		static public Type LookupType (DeclSpace ds, string name, bool silent, Location loc)
		{
			Type t;

			//
			// For the case the type we are looking for is nested within this one
			// or any base class
			//
			Type current_type = ds.TypeBuilder;

			while (current_type != null) {
				t = TypeManager.LookupType (current_type.FullName + "+" + name);
				if (t != null)
					return t;
				current_type = current_type.BaseType;
			} 

			t = TypeManager.LookupType (MakeFQN (ds.Namespace.Name, name));
			if (t != null)
				return t;

			// It's possible that name already is fully qualified. So we do
			// a simple direct lookup without adding any namespace names

			t = TypeManager.LookupType (name); 
			if (t != null)
				return t;
			
			for (Namespace ns = ds.Namespace; ns != null; ns = ns.Parent){
				ArrayList using_list = ns.UsingTable;

				if (using_list == null)
					continue;

				foreach (string n in using_list){
					t = TypeManager.LookupType (MakeFQN (n, name));
					if (t != null)
						return t;
				}
			}

			if (!silent)
				Report.Error (246, loc, "Cannot find type `"+name+"'");
			
			return null;
		}

		// <summary>
		//   This is the silent version of LookupType, you can use this
		//   to `probe' for a type
		// </summary>
		static public Type LookupType (TypeContainer tc, string name, Location loc)
		{
			return LookupType (tc, name, true, loc);
		}

		static public bool IsNamespace (string name)
		{
			Namespace ns;

			if (tree.Namespaces != null){
				ns = (Namespace) tree.Namespaces [name];

				if (ns != null)
					return true;
			}

			return false;
		}

		static void Report1530 (Location loc)
		{
			Report.Error (1530, loc, "Keyword new not allowed for namespace elements");
		}
		
		// <summary>
		//   Populates the structs and classes with fields and methods
		// </summary>
		//
		// This is invoked after all interfaces, structs and classes
		// have been defined through `ResolveTree' 
		static public void PopulateTypes ()
		{
			TypeContainer root = Tree.Types;

			if (interface_resolve_order != null){
				foreach (Interface iface in interface_resolve_order)
					if ((iface.ModFlags & Modifiers.NEW) == 0)
						iface.Define (root);
					else
						Report1530 (iface.Location);
			}


			if (type_container_resolve_order != null){
				foreach (TypeContainer tc in type_container_resolve_order)
					if ((tc.ModFlags & Modifiers.NEW) == 0)
						tc.Define (root);
					else
						Report1530 (tc.Location);
			}

			ArrayList delegates = root.Delegates;
			if (delegates != null){
				foreach (Delegate d in delegates)
					if ((d.ModFlags & Modifiers.NEW) == 0)
						d.Define (root);
					else
						Report1530 (d.Location);
			}

			ArrayList enums = root.Enums;
			if (enums != null){
				foreach (Enum en in enums)
					if ((en.ModFlags & Modifiers.NEW) == 0)
						en.Define (root);
					else
						Report1530 (en.Location);
			}
		}

		static public void EmitCode ()
		{
			if (type_container_resolve_order != null){
				foreach (TypeContainer tc in type_container_resolve_order)
					tc.EmitConstants ();
				
				foreach (TypeContainer tc in type_container_resolve_order)
					tc.Emit ();
			}
		}
		
		// <summary>
		//   Compiling against Standard Libraries property.
		// </summary>
		static public bool StdLib {
			get {
				return stdlib;
			}

			set {
				stdlib = value;
			}
		}

		static public ModuleBuilder ModuleBuilder {
			get {
				return mb;
			}
		}

		//
		// Public Field, used to track which method is the public entry
		// point.
		//
		static public MethodInfo EntryPoint;

		//
		// These are used to generate unique names on the structs and fields.
		//
		static int field_count;
		
		//
		// Makes an initialized struct, returns the field builder that
		// references the data.  Thanks go to Sergey Chaban for researching
		// how to do this.  And coming up with a shorter mechanism than I
		// was able to figure out.
		//
		// This works but makes an implicit public struct $ArrayType$SIZE and
		// makes the fields point to it.  We could get more control if we did
		// use instead:
		//
		// 1. DefineNestedType on the impl_details_class with our struct.
		//
		// 2. Define the field on the impl_details_class
		//
		static public FieldBuilder MakeStaticData (byte [] data)
		{
			FieldBuilder fb;
			int size = data.Length;
			
			if (impl_details_class == null)
				impl_details_class = mb.DefineType (
					"<PrivateImplementationDetails>", TypeAttributes.NotPublic);

			fb = impl_details_class.DefineInitializedData (
				"$$field-" + (field_count++), data,
				FieldAttributes.Static | FieldAttributes.Assembly);
			
			return fb;
		}
	}
}
	      

