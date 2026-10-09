using Marketplace.Domain.Common;

namespace Marketplace.Domain.Shipping;

public sealed class CustomerAddress : AggregateRoot<long>
{
    private CustomerAddress() { }
    public long CustomerId { get; private set; }
    public long CityId { get; private set; }
    public string RecipientName { get; private set; } = null!;
    public string RecipientMobile { get; private set; } = null!;
    public string AddressLine { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;
    public string? DeliveryNote { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerAddress Create(long id,long customerId,long cityId,string recipientName,string recipientMobile,string addressLine,string postalCode,string? deliveryNote,bool isDefault)
    {
        if(id<=0||customerId<=0||cityId<=0)throw new DomainException("Invalid address identifiers.");
        var values=Validate(recipientName,recipientMobile,addressLine,postalCode,deliveryNote);
        var now=DateTime.UtcNow;
        return new CustomerAddress{Id=id,CustomerId=customerId,CityId=cityId,RecipientName=values.recipient,RecipientMobile=values.mobile,AddressLine=values.address,PostalCode=values.postal,DeliveryNote=values.note,IsDefault=isDefault,CreatedAtUtc=now,UpdatedAtUtc=now};
    }

    public void Update(long cityId,string recipientName,string recipientMobile,string addressLine,string postalCode,string? deliveryNote,bool isDefault)
    {
        if(cityId<=0)throw new DomainException("A destination city is required.");
        var values=Validate(recipientName,recipientMobile,addressLine,postalCode,deliveryNote);
        CityId=cityId;RecipientName=values.recipient;RecipientMobile=values.mobile;AddressLine=values.address;PostalCode=values.postal;DeliveryNote=values.note;IsDefault=isDefault;UpdatedAtUtc=DateTime.UtcNow;
    }
    public void SetDefault(bool value){IsDefault=value;UpdatedAtUtc=DateTime.UtcNow;}
    private static (string recipient,string mobile,string address,string postal,string? note) Validate(string recipient,string mobile,string address,string postal,string? note)
    {
        recipient=recipient?.Trim()??"";mobile=mobile?.Trim()??"";address=address?.Trim()??"";postal=postal?.Trim()??"";note=string.IsNullOrWhiteSpace(note)?null:note.Trim();
        if(recipient.Length is <2 or >150)throw new DomainException("Recipient name must contain 2 to 150 characters.");
        if(mobile.Length is <8 or >30)throw new DomainException("Recipient mobile is invalid.");
        if(address.Length is <5 or >1000)throw new DomainException("Address line must contain 5 to 1000 characters.");
        if(postal.Length is <5 or >20)throw new DomainException("Postal code is invalid.");
        if(note?.Length>500)throw new DomainException("Delivery note cannot exceed 500 characters.");
        return(recipient,mobile,address,postal,note);
    }
}
