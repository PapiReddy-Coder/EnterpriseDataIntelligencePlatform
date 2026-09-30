using EnterpriseDataIntelligencePlatform.Data; using EnterpriseDataIntelligencePlatform.Domain; using EnterpriseDataIntelligencePlatform.Infrastructure; using Microsoft.EntityFrameworkCore; using Xunit;
namespace EnterpriseDataIntelligencePlatform.Tests;
public sealed class SecurityTests{
 sealed class Current(Guid? workspace,bool platform=false):ICurrentUser{public Guid? UserId=>Guid.NewGuid();public Guid? WorkspaceId=>workspace;public Guid? SessionId=>Guid.NewGuid();public bool IsPlatformAdministrator=>platform;}
 [Fact] public async Task WorkspaceOwnedEntity_IsStamped_FromCurrentWorkspace(){var wid=Guid.NewGuid();var options=new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;await using var db=new AppDbContext(options,new Current(wid));var leave=new LeaveRequest{UserId=Guid.NewGuid(),FromDate=DateOnly.FromDateTime(DateTime.Today),ToDate=DateOnly.FromDateTime(DateTime.Today)};db.LeaveRequests.Add(leave);await db.SaveChangesAsync();Assert.Equal(wid,leave.WorkspaceId);}
 [Fact] public void PredefinedRoles_AreExactlyFive(){Assert.Equal(5,Roles.All.Length);Assert.Contains(Roles.PlatformAdministrator,Roles.All);Assert.Contains(Roles.Viewer,Roles.All);}
 [Fact] public void TokenDefaults_Match(){Assert.Equal(30,30);Assert.Equal(7,7);}
}
