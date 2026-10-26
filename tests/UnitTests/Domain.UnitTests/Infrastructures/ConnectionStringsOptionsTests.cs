using System.ComponentModel.DataAnnotations;
using Domain.Infrastructures;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Infrastructures;

[TestFixture]
public class ConnectionStringsOptionsTests
{    
    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void ValidateObject_InvalidNeon_ReturnsValidationError(string value)
    {
        //Arrange
        var obj = new ConnectionStringsOptions
        {
            Neon = value
        };
        var context = new ValidationContext(obj);
        var results = new List<ValidationResult>();

        //Act
        var valid = Validator.TryValidateObject(obj, context, results, validateAllProperties: true);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid, Is.False);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].ErrorMessage, Is.EqualTo("The Neon field is required."));
        }        
    }

    [Test]
    public void ValidateObject_IsValid_ReturnsValid()
    {
        //Arrange
        var obj = new ConnectionStringsOptions
        {
            Neon = "Host=localhost"
        };
        var context = new ValidationContext(obj);
        var results = new List<ValidationResult>();

        //Act
        var valid = Validator.TryValidateObject(obj, context, results, validateAllProperties: true);

        //Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid, Is.True);
            Assert.That(results, Has.Count.Zero);
        }        
    }
}
