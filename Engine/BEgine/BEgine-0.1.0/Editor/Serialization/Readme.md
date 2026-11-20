# Serialization

Serialization contracts, asset and project document data, detached editing and validation,
plus YAML, JSON and XML formats. `BEngine.Serialization` is the runtime contract assembly;
`BEngine.Serialization.Yaml` supplies the host's editable YAML codec.

Use `JsonSerialization.Serialize` / `Deserialize` for JSON and
`XmlSerialization.Serialize` / `Deserialize` for XML. Both formats support
`SerializationMember`, `SerializationIgnore`, `Fix64`, and
`SerializationDataAdapter`. XML uses a versioned `serialization` root with
typed object, array, and scalar nodes; document type definitions are rejected.
