using Pos.Core.Invoicing;

namespace Pos.Core.Tests;

public class NifValidatorTests
{
    [Theory]
    [InlineData("12345678Z")]      // DNI
    [InlineData("12345678-z")]     // con guion y minúscula
    [InlineData("X1234567L")]      // NIE
    [InlineData("Y1234567X")]
    [InlineData("B12345674")]      // sociedad limitada: control numérico
    [InlineData("Q2826000H")]      // organismo público: control con letra
    [InlineData("ESB12345674")]    // con prefijo de país
    [InlineData("K1234567L")]      // NIF especial
    public void AcceptsValidNifs(string nif)
    {
        Assert.True(NifValidator.IsValid(nif));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678A")]      // letra de control incorrecta
    [InlineData("X1234567A")]
    [InlineData("B12345675")]      // dígito de control incorrecto
    [InlineData("B1234567D")]      // una S.L. no lleva letra de control
    [InlineData("Q2826000I")]
    [InlineData("1234567Z")]       // corto
    [InlineData("I12345674")]      // letra de entidad que no existe
    public void RejectsInvalidNifs(string nif)
    {
        Assert.False(NifValidator.IsValid(nif));
    }

    [Fact]
    public void Normalize_RemovesSeparatorsAndCountryPrefix()
    {
        Assert.Equal("B12345674", NifValidator.Normalize(" es b-1234.5674 "));
    }
}
