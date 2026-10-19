using Mono.Cecil;
using System.Text.Json;
using System.Security.Cryptography;
if(args[0]=="crc32c") {
 var assembly=System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[1]));
 var method=assembly.GetType("Fika.Core.Networking.LiteNetLib.Utils.CRC32C",true)!.GetMethod("Compute",new[]{typeof(byte[]),typeof(int),typeof(int)})!;
 var content=File.ReadAllBytes(args[2]);
 var actual=(uint)method.Invoke(null,new object[]{content,0,content.Length})!;
 if(actual!=uint.Parse(args[3]))throw new InvalidOperationException("Native Fika CRC32C mismatch");
 Console.WriteLine(JsonSerializer.Serialize(new{status="PASS_ACTUAL_FIKA_CRC32C_NOT_GAMEPLAY",crc32c=actual}));return;
}
var path=Path.GetFullPath(args[0]);
using var module=ModuleDefinition.ReadModule(path);
IEnumerable<TypeDefinition> Walk(TypeDefinition t){yield return t;foreach(var c in t.NestedTypes)foreach(var x in Walk(c))yield return x;}
var types=module.Types.SelectMany(Walk).Where(t=>args.Skip(1).Any(s=>t.Name==s||t.FullName==s));
Console.WriteLine(JsonSerializer.Serialize(new{file=Path.GetFileName(path),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),types=types.Select(t=>new{t.FullName,fields=t.Fields.Select(f=>new{f.Name,type=f.FieldType.FullName,constant=f.HasConstant?f.Constant:null}),properties=t.Properties.Select(p=>new{p.Name,type=p.PropertyType.FullName,set=p.SetMethod?.FullName}),methods=t.Methods.Where(m=>!m.IsGetter&&!m.IsSetter).Select(m=>new{m.FullName,il=m.HasBody?m.Body.Instructions.Select(i=>i.ToString()).ToArray():Array.Empty<string>()})})},new JsonSerializerOptions{WriteIndented=true}));
