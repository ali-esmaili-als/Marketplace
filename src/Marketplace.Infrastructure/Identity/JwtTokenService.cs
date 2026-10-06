using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marketplace.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace Marketplace.Infrastructure.Identity;
internal sealed class JwtTokenService(IOptions<JwtOptions> options)
{
 private readonly JwtOptions _options=options.Value;
 public (string Token,DateTime ExpiresAtUtc) CreateAccessToken(User user){var now=DateTime.UtcNow;var exp=now.AddMinutes(_options.AccessTokenMinutes);var claims=new[]{new Claim(JwtRegisteredClaimNames.Sub,user.Id.ToString()),new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.MobilePhone,user.Mobile??""),new Claim(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString("N")),new Claim("security_stamp",user.SecurityStamp)};var key=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));var token=new JwtSecurityToken(_options.Issuer,_options.Audience,claims,now,exp,new SigningCredentials(key,SecurityAlgorithms.HmacSha256));return(new JwtSecurityTokenHandler().WriteToken(token),exp);}
 public (string Raw,byte[] Hash,DateTime ExpiresAtUtc) CreateRefreshToken(){var raw=Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));return(raw,SHA256.HashData(Encoding.UTF8.GetBytes(raw)),DateTime.UtcNow.AddDays(_options.RefreshTokenDays));}
 public static byte[] HashRefreshToken(string raw)=>SHA256.HashData(Encoding.UTF8.GetBytes(raw));
}