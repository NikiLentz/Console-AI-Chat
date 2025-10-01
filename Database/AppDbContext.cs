using ConsoleAIChat.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ConsoleAIChat.Database;

public class AppDbContext:DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
    
    
    public DbSet<ChatMessage> ChatMessages { get; set; }
}