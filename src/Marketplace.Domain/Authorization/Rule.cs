using Marketplace.Domain.Common;
namespace Marketplace.Domain.Authorization;
public sealed class Rule : Entity<long>
{
 private Rule(){}
 public string Code{get;private set;}=null!;
 public string Name{get;private set;}=null!;
 public byte RuleType{get;private set;}
 public bool IsActive{get;private set;}
 public static Rule Create(long id,string code,string name,byte ruleType)=>new(){Id=id,Code=code.Trim(),Name=name.Trim(),RuleType=ruleType,IsActive=true};
 public void Disable()=>IsActive=false;
 public void Enable()=>IsActive=true;
}