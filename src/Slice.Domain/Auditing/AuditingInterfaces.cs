namespace Slice.Domain.Auditing;

public interface IHasConcurrencyStamp { string ConcurrencyStamp { get; set; } }

public interface IHasCreationTime { DateTime CreationTime { get; set; } }
public interface IMayHaveCreator { Guid? CreatorId { get; set; } }
public interface ICreationAuditedObject : IHasCreationTime, IMayHaveCreator;

public interface IHasModificationTime { DateTime? LastModificationTime { get; set; } }
public interface IModificationAuditedObject : IHasModificationTime { Guid? LastModifierId { get; set; } }

public interface IAuditedObject : ICreationAuditedObject, IModificationAuditedObject;

public interface ISoftDelete { bool IsDeleted { get; set; } }
public interface IHasDeletionTime : ISoftDelete { DateTime? DeletionTime { get; set; } Guid? DeleterId { get; set; } }
public interface IDeletionAuditedObject : IHasDeletionTime;

public interface IFullAuditedObject : IAuditedObject, IDeletionAuditedObject;
