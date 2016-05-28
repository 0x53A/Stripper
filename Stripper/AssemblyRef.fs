module Hydra.ReferenceAssembly

open System.Collections.Generic
open System
open Mono.Cecil
open Mono.Cecil.Cil
open System.IO
open System.Text
open System.Security.Cryptography

let createRefAsm (``in``:Stream) (out:Stream) =
    
    let rec flatten map root = seq {
        yield root
        for x in map root do
            yield! flatten map x
    }

    let assertTrue x = if not x then failwith "Expected TRUE"
    
    let asm = AssemblyDefinition.ReadAssembly(``in``)

    for _module in asm.Modules do
        let noInternalsVisibleTo = _module.CustomAttributes
                                    |> Seq.exists(fun a -> a.AttributeType.Name = "InternalsVisibleToAttribute")
                                    |> not
        
        let isPrivateType (t:TypeDefinition) =
            let isVisible = t.IsPublic ||
                            t.IsNested && t.IsNestedAssembly && (not noInternalsVisibleTo)
            not isVisible
        let isPrivateMethod (m:MethodDefinition) = m.IsPrivate || (noInternalsVisibleTo && m.IsAssembly)
        let isPrivateField (f:FieldDefinition) = f.IsPrivate || (noInternalsVisibleTo && f.IsAssembly)
        let notImplementedExceptionCtor =
            // todo: this may add a wrong assembly-ref
            let excType = typeof<NotImplementedException>
            let ctor = excType.GetConstructor([||])
            _module.Import(ctor)
        let allTypes = _module.Types |> Seq.collect (flatten (fun t -> t.NestedTypes))
        let fieldsInValueTypes = allTypes
                                // get public value types
                                |> Seq.filter (fun t -> t.IsValueType)
                                |> Seq.filter (fun t -> not (isPrivateType t))
                                // get fields
                                |> Seq.collect (fun t -> t.Fields)
                                |> Seq.map (fun f -> f.FieldType)
                                |> Seq.filter (fun t -> t.IsValueType)
                                |> HashSet
        

        for t in allTypes |> Seq.toArray do
            let canRemove = (isPrivateType t) && ((not t.IsValueType) || (not <| fieldsInValueTypes.Contains(t)))
            if canRemove then
                if t.IsNested then
                    t.DeclaringType.NestedTypes.Remove t |> assertTrue
                else
                    _module.Types.Remove(t) |> assertTrue
            else
                for m in t.Methods |> Seq.toArray do
                    if isPrivateMethod m then
                        // remove private methods
                        t.Methods.Remove(m) |> assertTrue
                    else
                        // replace public methods with 'throw NotImplementedException'
                        if m.HasBody then
                            let ilWriter = m.Body.GetILProcessor()
                            for i in m.Body.Instructions |> Seq.rev |> Seq.toArray do
                                ilWriter.Remove i
                            ilWriter.Append(ilWriter.Create(OpCodes.Newobj, notImplementedExceptionCtor))
                            ilWriter.Append(ilWriter.Create(OpCodes.Throw))
                            m.Body.Variables.Clear()                            
                for f in t.Fields |> Seq.toArray do
                    if not (t.IsValueType) then
                        if isPrivateField f then
                            // remove private fields from classes
                            t.Fields.Remove(f) |> assertTrue
                    else // value type
                        if isPrivateField f &&
                           (not f.FieldType.IsValueType) &&
                           (not f.FieldType.IsGenericParameter)
                        then
                            // change the type of reference-typed private fields to object
                            f.FieldType <- _module.TypeSystem.Object
        _module.Mvid <- _module.FullyQualifiedName
                        |> Encoding.UTF8.GetBytes
                        |> MD5.Create().ComputeHash
                        |> Guid
        _module.Resources.Clear()
    asm.Write(out, WriterParameters(WriteSymbols=false))
        