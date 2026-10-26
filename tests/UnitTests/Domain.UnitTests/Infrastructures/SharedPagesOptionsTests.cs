using System.ComponentModel.DataAnnotations;
using Domain.Infrastructures;
using Domain.UnitTests.TestSupport;

namespace Domain.UnitTests.Infrastructures;

[TestFixture]
public class SharedPagesOptionsTests
{
    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public void ValidateObject_InvalidSecret_ReturnsValidationError(string value)
    {
        //Arrange
        var obj = new SharedPagesOptions
        {
            Secret = value
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
            Assert.That(results[0].ErrorMessage, Is.EqualTo("The Secret field is required."));
        }        
    }

    [Test]
    public void ValidateObject_IsValid_ReturnsValid()
    {
        //Arrange
        var obj = new SharedPagesOptions
        {
            Secret = "f0ad98af8f0a9d09f08ad0af80a8d09f80d9a8f0a980a"
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
