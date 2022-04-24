using System.Text;
using Bogus;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CSharp;

namespace _42.Monorepo.Repo.Generator;

internal class ClassMethodGenerator : CSharpSyntaxRewriter
{
    private static readonly Faker Faker = new();

    public string MethodName { get; private set; } = string.Empty;

    public int ParametersCount { get; private set; }

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        // TODO: [P1] possibly add reference to new dependency
        return base.VisitUsingDirective(node);
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var workType = SyntaxFactory.IdentifierName("decimal");
        MethodName = Faker.Random.GetValidName();
        var modifiers = SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        var @params = SyntaxFactory.ParameterList();

        ParametersCount = Faker.Random.Int(0, 4);
        for (var i = 0; i < ParametersCount; i++)
        {
            var name = SyntaxFactory.Identifier($"p{i + 1}");
            var paramSyntax = SyntaxFactory.Parameter(
                default,
                SyntaxFactory.TokenList(),
                workType,
                name,
                null);
            @params = @params.AddParameters(paramSyntax);
        }

        var body = new StringBuilder($"return {Faker.Random.Decimal() * Faker.Random.Int(0, 999)}m");

        for (var i = 0; i < ParametersCount; i++)
        {
            body.Append(Faker.Random.Bool(0.666f) ? " + " : " - ");
            body.Append($"p{i + 1}");
        }

        body.Append(';');

        var bodyStatement = SyntaxFactory
            .Block(SyntaxFactory.ParseStatement(body.ToString()));

        var newMethodIndex = Faker.Random.Int(0, node.Members.Count);
        var newMethod = SyntaxFactory.MethodDeclaration(workType, MethodName)
            .WithModifiers(modifiers)
            .WithParameterList(@params)
            .WithBody(bodyStatement);

        return node.WithMembers(node.Members.Insert(newMethodIndex, newMethod));
    }
}
