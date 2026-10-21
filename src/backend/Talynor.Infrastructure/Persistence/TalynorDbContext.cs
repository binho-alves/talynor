using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Talynor.Domain.Workspaces;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.KnowledgeContents;
using Talynor.Domain.StudySessions;
using Talynor.Domain.Flashcards;
using Talynor.Domain.Quizzes;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Talynor.Infrastructure.Persistence;

public sealed class TalynorDbContext(DbContextOptions<TalynorDbContext> options)
    : IdentityUserContext<IdentityUser>(options)
{
    public DbSet<PersonalWorkspace> PersonalWorkspaces => Set<PersonalWorkspace>();
    public DbSet<KnowledgeMap> KnowledgeMaps => Set<KnowledgeMap>();
    public DbSet<KnowledgeNode> KnowledgeNodes => Set<KnowledgeNode>();
    public DbSet<KnowledgeEdge> KnowledgeEdges => Set<KnowledgeEdge>();
    public DbSet<KnowledgeContent> KnowledgeContents => Set<KnowledgeContent>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<Flashcard> Flashcards => Set<Flashcard>();
    public DbSet<FlashcardReviewState> FlashcardReviewStates => Set<FlashcardReviewState>();
    public DbSet<FlashcardReview> FlashcardReviews => Set<FlashcardReview>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizOption> QuizOptions => Set<QuizOption>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var utc = new ValueConverter<DateTimeOffset, DateTime>(value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Entity<Quiz>(quiz =>
        {
            quiz.ToTable("Quizzes", table => table.HasCheckConstraint("CK_Quizzes_ContentRevision", "ContentRevision > 0"));
            quiz.HasKey(q => q.Id);
            quiz.Property(q => q.Id).ValueGeneratedNever();
            quiz.HasOne<KnowledgeNode>().WithMany().HasForeignKey(q => q.KnowledgeNodeId).OnDelete(DeleteBehavior.Cascade);
            quiz.Property(q => q.Title).IsRequired().HasMaxLength(200);
            quiz.Property(q => q.CreatedAtUtc).HasConversion(utc);
            quiz.Property(q => q.UpdatedAtUtc).HasConversion(utc);
            quiz.HasIndex(q => new { q.KnowledgeNodeId, q.CreatedAtUtc, q.Id });
            quiz.HasMany(q => q.Questions).WithOne().HasForeignKey(q => q.QuizId).OnDelete(DeleteBehavior.Cascade);
            quiz.Navigation(q => q.Questions).HasField("questions").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<QuizQuestion>(question =>
        {
            question.ToTable("QuizQuestions", table => table.HasCheckConstraint("CK_QuizQuestions_Position", "Position BETWEEN 1 AND 10"));
            question.HasKey(q => q.Id);
            question.Property(q => q.Id).ValueGeneratedNever();
            question.Property(q => q.Text).IsRequired().HasMaxLength(1000);
            question.HasIndex(q => new { q.QuizId, q.Position }).IsUnique();
            question.HasMany(q => q.Options).WithOne().HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
            question.Navigation(q => q.Options).HasField("options").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<QuizOption>(option =>
        {
            option.ToTable("QuizOptions", table =>
            {
                table.HasCheckConstraint("CK_QuizOptions_Position", "Position BETWEEN 1 AND 4");
                table.HasCheckConstraint("CK_QuizOptions_IsCorrect", "IsCorrect IN (0,1)");
            });
            option.HasKey(o => o.Id);
            option.Property(o => o.Id).ValueGeneratedNever();
            option.Property(o => o.Text).IsRequired().HasMaxLength(500);
            option.HasIndex(o => new { o.QuestionId, o.Position }).IsUnique();
            option.HasIndex(o => o.QuestionId).IsUnique().HasFilter("IsCorrect = 1");
        });
        builder.Entity<Flashcard>(card =>
        {
            card.ToTable("Flashcards", table => table.HasCheckConstraint("CK_Flashcards_ContentRevision", "ContentRevision > 0"));
            card.HasKey(value => value.Id);
            card.Property(value => value.Id).ValueGeneratedNever();
            card.HasOne<KnowledgeNode>().WithMany().HasForeignKey(value => value.KnowledgeNodeId)
                .OnDelete(DeleteBehavior.Cascade);
            card.Property(value => value.Front).IsRequired().HasMaxLength(2000);
            card.Property(value => value.Back).IsRequired().HasMaxLength(4000);
            card.Property(value => value.ContentRevision).IsRequired();
            var utc = new ValueConverter<DateTimeOffset, DateTime>(value => value.UtcDateTime,
                value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            card.Property(value => value.CreatedAtUtc).HasConversion(utc);
            card.Property(value => value.UpdatedAtUtc).HasConversion(utc);
            card.HasIndex(value => new { value.KnowledgeNodeId, value.CreatedAtUtc, value.Id });
        });
        builder.Entity<FlashcardReviewState>(state =>
        {
            state.ToTable("FlashcardReviewStates", table =>
            {
                table.HasCheckConstraint("CK_FlashcardReviewStates_ReviewCounts",
                    "ReviewCount >= 1 AND CorrectCount >= 0 AND CorrectCount <= ReviewCount");
                table.HasCheckConstraint("CK_FlashcardReviewStates_LastReviewedAtUtc", "LastReviewedAtUtc IS NOT NULL");
            });
            state.HasKey(value => value.FlashcardId);
            state.Property(value => value.FlashcardId).ValueGeneratedNever();
            state.Property(value => value.ReviewCount).IsRequired();
            state.Property(value => value.CorrectCount).IsRequired();
            state.Property(value => value.LastReviewedAtUtc).IsRequired().HasConversion(utc);
            state.HasOne<Flashcard>().WithOne().HasForeignKey<FlashcardReviewState>(value => value.FlashcardId)
                .OnDelete(DeleteBehavior.Cascade);
            state.HasIndex(value => new { value.LastReviewedAtUtc, value.FlashcardId });
        });
        builder.Entity<FlashcardReview>(review =>
        {
            review.ToTable("FlashcardReviews", table =>
            {
                table.HasCheckConstraint("CK_FlashcardReviews_ContentRevision", "ContentRevision > 0");
                table.HasCheckConstraint("CK_FlashcardReviews_Result", "Result IN ('correct', 'incorrect')");
            });
            review.HasKey(value => value.ReviewId);
            review.Property(value => value.ReviewId).ValueGeneratedNever();
            review.Property(value => value.FlashcardId).IsRequired();
            review.Property(value => value.ContentRevision).IsRequired();
            review.Property(value => value.Result).IsRequired().HasMaxLength(9).HasConversion(
                value => ReviewResultToStorage(value), value => ReviewResultFromStorage(value));
            review.Property(value => value.ReviewedAtUtc).IsRequired().HasConversion(utc);
            review.HasOne<Flashcard>().WithMany().HasForeignKey(value => value.FlashcardId)
                .OnDelete(DeleteBehavior.Cascade);
            review.HasIndex(value => new { value.FlashcardId, value.ReviewedAtUtc, value.ReviewId })
                .IsDescending(false, true, true);
        });
        builder.Entity<StudySession>(session =>
        {
            session.ToTable("StudySessions", table => table.HasCheckConstraint("CK_StudySessions_TimeRange",
                "EndedAtUtc IS NULL OR EndedAtUtc >= StartedAtUtc"));
            session.HasKey(value => value.Id);
            session.Property(value => value.Id).ValueGeneratedNever();
            session.HasOne<KnowledgeNode>().WithMany().HasForeignKey(value => value.KnowledgeNodeId)
                .OnDelete(DeleteBehavior.Restrict);
            session.Property(value => value.StartedAtUtc).HasConversion(utc);
            session.Property(value => value.EndedAtUtc).HasConversion(utc);
            session.Ignore(value => value.Duration);
            session.Ignore(value => value.Status);
            session.HasIndex(value => new { value.KnowledgeNodeId, value.StartedAtUtc, value.Id })
                .IsDescending(false, true, true);
        });
        builder.Entity<KnowledgeContent>(content =>
        {
            content.ToTable("KnowledgeContents", table =>
            {
                table.HasCheckConstraint("CK_KnowledgeContents_Type", "Type IN ('note', 'link')");
                table.HasCheckConstraint("CK_KnowledgeContents_Payload",
                    "(Type = 'note' AND Body IS NOT NULL AND Url IS NULL) OR (Type = 'link' AND Url IS NOT NULL AND Body IS NULL)");
            });
            content.HasKey(value => value.Id);
            content.Property(value => value.Id).ValueGeneratedNever();
            content.HasOne<KnowledgeNode>().WithMany().HasForeignKey(value => value.KnowledgeNodeId)
                .OnDelete(DeleteBehavior.Cascade);
            content.Property(value => value.Type).IsRequired().HasConversion(
                value => ContentTypeToStorage(value), value => ContentTypeFromStorage(value));
            content.Property(value => value.Title).HasMaxLength(200);
            content.Property(value => value.Body).HasMaxLength(4000);
            content.Property(value => value.Url).HasMaxLength(2048);
            // New content timestamps only: order by UTC DateTime in SQLite, retain UTC DateTimeOffset in Domain/API.
            content.Property(value => value.CreatedAtUtc).HasConversion(utc);
            content.Property(value => value.UpdatedAtUtc).HasConversion(utc);
            content.HasIndex(value => new { value.KnowledgeNodeId, value.CreatedAtUtc, value.Id });
        });
        builder.Entity<PersonalWorkspace>(workspace =>
        {
            workspace.ToTable("PersonalWorkspaces");
            workspace.HasKey(value => value.Id);
            workspace.Property(value => value.Id).ValueGeneratedNever();
            workspace.Property(value => value.OwnerUserId).IsRequired();
            workspace.HasIndex(value => value.OwnerUserId).IsUnique();
            workspace.Property(value => value.CreatedAtUtc).IsRequired();
            // Logical Identity reference only; account-deletion lifecycle remains outside this slice.
        });
        builder.Entity<KnowledgeMap>(map =>
        {
            map.ToTable("KnowledgeMaps");
            map.HasKey(value => value.Id);
            map.Property(value => value.Id).ValueGeneratedNever();
            map.Property(value => value.WorkspaceId).IsRequired();
            map.HasIndex(value => value.WorkspaceId);
            map.HasOne<PersonalWorkspace>().WithMany().HasForeignKey(value => value.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            map.Property(value => value.Title).IsRequired().HasMaxLength(200);
            map.Property(value => value.Description).HasMaxLength(4000);
            map.Property(value => value.CreatedAtUtc).IsRequired();
            map.Property(value => value.UpdatedAtUtc).IsRequired();
        });
        builder.Entity<KnowledgeNode>(node =>
        {
            node.ToTable("KnowledgeNodes", table =>
            {
                table.HasTrigger("TR_KnowledgeNodes_ChildWorkspace_Insert");
                table.HasTrigger("TR_KnowledgeNodes_ChildWorkspace_Update");
                table.HasCheckConstraint("CK_KnowledgeNodes_FinitePositions",
                    "abs(PositionX) <= 1.7976931348623157e308 AND abs(PositionY) <= 1.7976931348623157e308");
            });
            node.HasKey(value => value.Id);
            node.Property(value => value.Id).ValueGeneratedNever();
            // Composite key makes each edge endpoint's map membership a database constraint.
            node.HasAlternateKey(value => new { value.KnowledgeMapId, value.Id });
            node.HasOne<KnowledgeMap>().WithMany().HasForeignKey(value => value.KnowledgeMapId)
                .OnDelete(DeleteBehavior.Cascade);
            node.HasOne<KnowledgeMap>().WithMany().HasForeignKey(value => value.ChildMapId)
                .OnDelete(DeleteBehavior.Restrict);
            node.Property(value => value.Title).IsRequired().HasMaxLength(200);
            node.Property(value => value.Description).HasMaxLength(4000);
            node.Property(value => value.PositionX).IsRequired();
            node.Property(value => value.PositionY).IsRequired();
            node.Property(value => value.CreatedAtUtc).IsRequired();
            node.Property(value => value.UpdatedAtUtc).IsRequired();
        });
        builder.Entity<KnowledgeMap>().ToTable("KnowledgeMaps", table =>
            table.HasTrigger("TR_KnowledgeMaps_ChildWorkspace_Update"));
        builder.Entity<KnowledgeEdge>(edge =>
        {
            edge.ToTable("KnowledgeEdges");
            edge.HasKey(value => value.Id);
            edge.Property(value => value.Id).ValueGeneratedNever();
            edge.HasOne<KnowledgeMap>().WithMany().HasForeignKey(value => value.KnowledgeMapId)
                .OnDelete(DeleteBehavior.Cascade);
            edge.HasOne<KnowledgeNode>().WithMany()
                .HasForeignKey(value => new { value.KnowledgeMapId, value.SourceNodeId })
                .HasPrincipalKey(value => new { value.KnowledgeMapId, value.Id })
                .OnDelete(DeleteBehavior.NoAction);
            edge.HasOne<KnowledgeNode>().WithMany()
                .HasForeignKey(value => new { value.KnowledgeMapId, value.TargetNodeId })
                .HasPrincipalKey(value => new { value.KnowledgeMapId, value.Id })
                .OnDelete(DeleteBehavior.NoAction);
            edge.Property(value => value.CreatedAtUtc).IsRequired();
        });
    }

    private static string ContentTypeToStorage(KnowledgeContentType value) => value switch
    {
        KnowledgeContentType.Note => "note", KnowledgeContentType.Link => "link",
        _ => throw new InvalidOperationException("Unknown knowledge content type.")
    };
    private static KnowledgeContentType ContentTypeFromStorage(string value) => value switch
    {
        "note" => KnowledgeContentType.Note, "link" => KnowledgeContentType.Link,
        _ => throw new InvalidOperationException("Unknown knowledge content type.")
    };

    private static string ReviewResultToStorage(FlashcardAnswerResult value) => value switch
    {
        FlashcardAnswerResult.Correct => "correct",
        FlashcardAnswerResult.Incorrect => "incorrect",
        _ => throw new InvalidOperationException("Unknown flashcard review result.")
    };

    private static FlashcardAnswerResult ReviewResultFromStorage(string value) => value switch
    {
        "correct" => FlashcardAnswerResult.Correct,
        "incorrect" => FlashcardAnswerResult.Incorrect,
        _ => throw new InvalidOperationException("Unknown stored flashcard review result.")
    };
}
