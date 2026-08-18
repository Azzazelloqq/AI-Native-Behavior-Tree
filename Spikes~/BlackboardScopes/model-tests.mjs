import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const fixture = name => JSON.parse(fs.readFileSync(path.join(here, "Fixtures", name), "utf8"));
function losslessIntegerFixture(name) {
  const source = fs.readFileSync(path.join(here, "Fixtures", name), "utf8");
  let transformed = "";
  let inString = false;
  let escaped = false;
  for (let index = 0; index < source.length;) {
    const character = source[index];
    if (inString) {
      transformed += character;
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') inString = false;
      index++;
      continue;
    }
    if (character === '"') {
      inString = true;
      transformed += character;
      index++;
      continue;
    }
    if (character === "-" || character >= "0" && character <= "9") {
      const token = source.slice(index).match(/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?/);
      if (!token) throw new Error("Invalid numeric token in lossless fixture.");
      const value = token[0];
      transformed += value.includes(".") || /[eE]/.test(value)
        ? value
        : `"__aibt_integer__${value}"`;
      index += value.length;
      continue;
    }
    transformed += character;
    index++;
  }
  return JSON.parse(transformed, (_, value) => typeof value === "string" && value.startsWith("__aibt_integer__")
    ? BigInt(value.slice("__aibt_integer__".length))
    : value);
}
const clone = value => structuredClone(value);
const sha256 = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
function float32FromBits(bitsHex) {
  const bytes = Buffer.alloc(4);
  bytes.writeUInt32LE(Number.parseInt(bitsHex, 16));
  return bytes.readFloatLE(0);
}
const idPattern = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const maxU64 = 18446744073709551615n;

const reductionCodes = Object.freeze({ none: 0, min: 1, max: 2, sum: 3, any: 4, all: 5, first: 6, last: 7 });
const scopeCodes = Object.freeze({ tree: 0, agent: 1, shared: 2 });
const accessModeCodes = Object.freeze({ read: 1, write: 2, readwrite: 3 });
const observerModeCodes = Object.freeze({ self: 1, "lower-priority": 2, both: 3 });
const numericTypes = new Set(["Int32", "Int64", "Float32", "Float64"]);
const builtInTypes = new Set([
  "Bool", "Int32", "Int64", "Float32", "Float64", "Float2", "Float3", "Quaternion",
  "Enum32", "FixedString32", "FixedString64", "FixedString128", "FixedString512",
  "AgentId", "EntityId", "OperationId", "AssetId"
]);
const registeredSchemas = Object.freeze({
  "example.Target": {
    version: 2,
    members: [
      { name: "entity", type: "Int32" },
      { name: "weight", type: "Float32" }
    ]
  }
});

let assertions = 0;
function check(condition, message) {
  assertions++;
  assert.ok(condition, message);
}

function equal(actual, expected, message) {
  assertions++;
  assert.deepEqual(actual, expected, message);
}

class Bytes {
  constructor() { this.parts = []; }
  raw(value) { this.parts.push(Buffer.from(value)); }
  u8(value) { const b = Buffer.alloc(1); b.writeUInt8(value); this.raw(b); }
  u16(value) { const b = Buffer.alloc(2); b.writeUInt16LE(value); this.raw(b); }
  u32(value) { const b = Buffer.alloc(4); b.writeUInt32LE(value); this.raw(b); }
  u64(value) { const b = Buffer.alloc(8); b.writeBigUInt64LE(BigInt(value)); this.raw(b); }
  bytes(value) { const b = Buffer.from(value); this.u32(b.length); this.raw(b); }
  text(value) { this.bytes(Buffer.from(value, "utf8")); }
  build() { return Buffer.concat(this.parts); }
}

function fnv1a64(value) {
  let hash = 14695981039346656037n;
  for (const octet of Buffer.from(value, "utf8")) {
    hash ^= BigInt(octet);
    hash = BigInt.asUintN(64, hash * 1099511628211n);
  }
  return hash;
}

function parseCanonicalUnsigned(value, maximum, nonzero) {
  try {
    if (typeof value === "number" && !Number.isSafeInteger(value)) return null;
    if (typeof value === "string" && !/^(0|[1-9][0-9]*)$/.test(value)) return null;
    const parsed = BigInt(value);
    if (parsed < 0n || parsed > maximum || nonzero && parsed === 0n) return null;
    return parsed;
  } catch {
    return null;
  }
}

function parseNonzeroU64(value) {
  return parseCanonicalUnsigned(value, maxU64, true);
}

function normalizeNumberText(value) {
  let text = value.toLowerCase();
  if (text.includes("e")) {
    let [mantissa, exponent] = text.split("e");
    exponent = exponent.replace(/^\+/, "").replace(/^(-?)0+(?=\d)/, "$1");
    if (mantissa.includes(".")) mantissa = mantissa.replace(/0+$/, "").replace(/\.$/, "");
    return `${mantissa}e${exponent}`;
  }
  return text.includes(".") ? text.replace(/0+$/, "").replace(/\.$/, "") : text;
}

function equivalentDecimalForms(value) {
  const normalized = normalizeNumberText(value);
  const match = /^(-?)([0-9]+)(?:\.([0-9]*))?(?:e(-?[0-9]+))?$/.exec(normalized);
  if (!match) throw new Error(`Invalid decimal candidate ${value}`);
  const sign = match[1];
  const integer = match[2];
  const fraction = match[3] ?? "";
  const exponent = Number(match[4] ?? 0);
  let digits = integer + fraction;
  let point = integer.length + exponent;
  const leadingZeros = /^0*/.exec(digits)[0].length;
  digits = digits.slice(leadingZeros);
  point -= leadingZeros;
  if (digits.length === 0) return ["0"];
  digits = digits.replace(/0+$/, "");

  const plainMagnitude = point <= 0
    ? `0.${"0".repeat(-point)}${digits}`
    : point >= digits.length
      ? `${digits}${"0".repeat(point - digits.length)}`
      : `${digits.slice(0, point)}.${digits.slice(point)}`;
  const scientificMantissa = digits.length === 1 ? digits : `${digits[0]}.${digits.slice(1)}`;
  const scientificExponent = point - 1;
  const scientificMagnitude = scientificExponent === 0
    ? scientificMantissa
    : `${scientificMantissa}e${scientificExponent}`;
  return [...new Set([
    normalizeNumberText(sign + plainMagnitude),
    normalizeNumberText(sign + scientificMagnitude)
  ])];
}

function canonicalFloat32(value) {
  const rounded = Math.fround(value);
  if (!Number.isFinite(rounded)) throw new Error("non-finite Float32");
  if (Object.is(rounded, -0)) return "0";
  const candidates = new Set(equivalentDecimalForms(rounded.toString()));
  for (let precision = 1; precision <= 9; precision++) {
    for (const source of [rounded.toPrecision(precision), rounded.toExponential(precision - 1)]) {
      for (const candidate of equivalentDecimalForms(source)) {
        if (/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:e-?[0-9]+)?$/.test(candidate)
          && Object.is(Math.fround(Number(candidate)), rounded)) {
          candidates.add(candidate);
        }
      }
    }
  }
  const ordered = [...candidates]
    .filter(candidate => Object.is(Math.fround(Number(candidate)), rounded))
    .sort((left, right) => left.length - right.length || (left < right ? -1 : left > right ? 1 : 0));
  if (ordered.length === 0) throw new Error("Float32 formatting failed");
  return ordered[0];
}

function canonicalFloat64(value) {
  if (!Number.isFinite(value)) throw new Error("non-finite Float64");
  if (Object.is(value, -0)) return "0";
  return normalizeNumberText(JSON.stringify(value));
}

function ordinalUtf8(left, right) {
  return Buffer.compare(Buffer.from(left, "utf8"), Buffer.from(right, "utf8"));
}

function canonicalJson(value, typeId = "") {
  if (value === null || typeof value === "boolean" || typeof value === "string") return JSON.stringify(value);
  if (typeof value === "bigint") return value.toString();
  if (typeof value === "number") {
    return typeId === "Float32" ? canonicalFloat32(value) : canonicalFloat64(value);
  }
  if (Array.isArray(value)) return `[${value.map(item => canonicalJson(item)).join(",")}]`;
  if (typeof value === "object") {
    const semanticOrder = typeId === "example.Target" ? ["entity", "weight"]
      : typeId === "Float2" ? ["x", "y"]
      : typeId === "Float3" ? ["x", "y", "z"]
      : typeId === "Quaternion" ? ["x", "y", "z", "w"]
      : typeId === "Enum32" ? ["contract", "value"]
      : typeId === "AssetId" ? ["guid", "localFileId"]
      : null;
    const keys = semanticOrder
      ? semanticOrder.filter(key => Object.hasOwn(value, key))
      : Object.keys(value).sort(ordinalUtf8);
    const memberType = key => typeId === "example.Target" && key === "weight" ? "Float32"
      : ["Float2", "Float3", "Quaternion"].includes(typeId) ? "Float32"
      : "";
    return `{${keys.map(key => `${JSON.stringify(key)}:${canonicalJson(value[key], memberType(key))}`).join(",")}}`;
  }
  throw new Error("unsupported canonical value");
}

function isExactInteger(value, minimum, maximum) {
  if (typeof value === "bigint") return value >= minimum && value <= maximum;
  return Number.isSafeInteger(value) && BigInt(value) >= minimum && BigInt(value) <= maximum;
}

function isFiniteNumber(value) {
  return typeof value === "number" && Number.isFinite(value);
}

function exactObject(value, names) {
  return value !== null && typeof value === "object" && !Array.isArray(value)
    && Object.keys(value).length === names.length
    && names.every(name => Object.hasOwn(value, name));
}

function validateTypedValue(entry, value) {
  const type = entry.type;
  if (builtInTypes.has(type) && entry.typeVersion !== 1) return "built-in typeVersion must be 1";
  switch (type) {
    case "Bool": return typeof value === "boolean" ? null : "Bool requires a Boolean";
    case "Int32": return isExactInteger(value, -2147483648n, 2147483647n) ? null : "Int32 is out of range";
    case "Int64": return isExactInteger(value, -9223372036854775808n, 9223372036854775807n) ? null : "Int64 is out of range or not exactly represented";
    case "Float32":
      return isFiniteNumber(value) && Number.isFinite(Math.fround(value)) ? null : "Float32 requires a finite representable value";
    case "Float64": return isFiniteNumber(value) ? null : "Float64 requires a finite value";
    case "Float2":
    case "Float3":
    case "Quaternion": {
      const names = type === "Float2" ? ["x", "y"] : type === "Float3" ? ["x", "y", "z"] : ["x", "y", "z", "w"];
      return exactObject(value, names)
        && names.every(name => isFiniteNumber(value[name]) && Number.isFinite(Math.fround(value[name])))
        ? null : `${type} requires exactly ${names.join(",")}`;
    }
    case "Enum32":
      return exactObject(value, ["contract", "value"])
        && value.contract === entry.enumContract
        && isExactInteger(value.value, -2147483648n, 2147483647n)
        ? null : "Enum32 default must match its contract and Int32 range";
    case "FixedString32":
    case "FixedString64":
    case "FixedString128":
    case "FixedString512": {
      const capacity = { FixedString32: 29, FixedString64: 61, FixedString128: 125, FixedString512: 509 }[type];
      return typeof value === "string" && Buffer.byteLength(value, "utf8") <= capacity
        ? null : `${type} UTF-8 capacity exceeded`;
    }
    case "AgentId":
    case "EntityId":
      return typeof value === "string" && parseNonzeroU64(value) !== null
        ? null : `${type} requires a canonical nonzero UInt64 decimal string`;
    case "OperationId": {
      if (typeof value !== "string") return "OperationId requires four colon-separated unsigned decimal fields";
      const parts = value.split(":");
      if (parts.length !== 4
        || parseNonzeroU64(parts[0]) === null
        || parseCanonicalUnsigned(parts[1], 0xffffffffn, false) === null || BigInt(parts[1]) === 0xffffffffn
        || parseCanonicalUnsigned(parts[2], 0xffffffffn, false) === null
        || parseCanonicalUnsigned(parts[3], maxU64, false) === null) {
        return "OperationId requires canonical treeId:nodeIndex:generation:sequence fields";
      }
      return null;
    }
    case "AssetId":
      return value !== null && typeof value === "object" && !Array.isArray(value)
        && /^[0-9a-f]{32}$/.test(value.guid ?? "")
        && Object.keys(value).every(key => key === "guid" || key === "localFileId")
        && (!Object.hasOwn(value, "localFileId") || isExactInteger(value.localFileId, -9223372036854775808n, 9223372036854775807n))
        ? null : "AssetId default is invalid";
    default: {
      const schema = registeredSchemas[type];
      if (!schema || entry.typeVersion !== schema.version) return "registered type/version has no canonical schema";
      if (!exactObject(value, schema.members.map(member => member.name))) return "registered default members are incomplete or unknown";
      for (const member of schema.members) {
        const memberError = validateTypedValue({ type: member.type, typeVersion: 1 }, value[member.name]);
        if (memberError) return `registered member ${member.name}: ${memberError}`;
      }
      return null;
    }
  }
}

function expectKeys(value, allowed, context) {
  for (const key of Object.keys(value)) {
    if (!allowed.includes(key)) throw new Error(`${context}: unknown property ${key}`);
  }
}

function validateTree(document, declaredSharedWrites = []) {
  const errors = [];
  const add = (code, pointer, message) => errors.push({ code, pointer, message });
  if (document?.format !== "aibt.tree" || document?.formatVersion !== 2) {
    add("AIBT2012", "/formatVersion", "Agent/Shared contracts require aibt.tree format version 2.");
    return errors;
  }

  try {
    expectKeys(document,
      ["format", "formatVersion", "treeId", "name", "description", "root", "blackboardContracts", "blackboard", "nodes", "tags", "metadata"],
      "tree");
  } catch (error) {
    add("AIBT1004", "", error.message);
  }

  if (!idPattern.test(document.treeId ?? "")) add("AIBT2013", "/treeId", "Invalid tree ID.");
  const contracts = document.blackboardContracts ?? {};
  const blackboard = document.blackboard ?? {};
  const scopesPresent = new Set(Object.values(blackboard).map(entry => entry.scope ?? "tree"));

  for (const scope of ["agent", "shared"]) {
    const descriptor = contracts[scope];
    if (scopesPresent.has(scope)) {
      if (!descriptor || !idPattern.test(descriptor.contractId ?? "")
        || !Number.isInteger(descriptor.contractVersion)
        || descriptor.contractVersion < 1
        || descriptor.contractVersion > 0xffffffff) {
        add("AIBT2042", `/blackboardContracts/${scope}`, `Invalid or missing ${scope} contract descriptor.`);
      }
    } else if (descriptor) {
      add("AIBT2042", `/blackboardContracts/${scope}`, `Unused ${scope} contract descriptor.`);
    }
  }

  for (const [keyId, entry] of Object.entries(blackboard)) {
    const pointer = `/blackboard/${keyId}`;
    if (!idPattern.test(keyId)) add("AIBT2001", pointer, "Invalid key ID.");
    if (!entry || typeof entry !== "object" || Array.isArray(entry)) {
      add("AIBT1004", pointer, "Blackboard entry must be an object.");
      continue;
    }
    try {
      expectKeys(entry, ["type", "typeVersion", "enumContract", "scope", "reduction", "description", "default"], pointer);
    } catch (error) {
      add("AIBT1004", pointer, error.message);
    }
    const scope = entry.scope ?? "tree";
    const extendedScope = scope === "agent" || scope === "shared";
    if (typeof entry.type !== "string" || entry.type.length === 0
      || (extendedScope && (!Number.isInteger(entry.typeVersion) || entry.typeVersion < 1 || entry.typeVersion > 0xffffffff))
      || !["tree", "agent", "shared"].includes(scope)
      || (extendedScope && !Object.hasOwn(entry, "default"))) {
      add("AIBT1004", pointer, "Invalid typed blackboard entry.");
    }
    if (entry.type === "Enum32") {
      if (!idPattern.test(entry.enumContract ?? "")) add("AIBT2003", `${pointer}/enumContract`, "Enum32 requires a contract.");
    } else if (Object.hasOwn(entry, "enumContract")) {
      add("AIBT2003", `${pointer}/enumContract`, "enumContract is forbidden for this type.");
    }

    if (Object.hasOwn(entry, "default")) {
      const valueError = validateTypedValue({ ...entry, scope, typeVersion: entry.typeVersion ?? 1 }, entry.default);
      if (valueError) add("AIBT2007", `${pointer}/default`, valueError);
    }

    const kind = entry.reduction?.kind;
    if (kind === "custom" || (kind !== undefined && !Object.hasOwn(reductionCodes, kind))) {
      add("AIBT2046", `${pointer}/reduction/kind`, "Custom or unknown reducers are unsupported in v1.");
    } else if (scope !== "shared" && entry.reduction !== undefined) {
      add("AIBT2045", `${pointer}/reduction`, "Reduction is allowed only for Shared scope.");
    } else if (["min", "max", "sum"].includes(kind) && !numericTypes.has(entry.type)) {
      add("AIBT2045", `${pointer}/reduction`, "Numeric reducer/type mismatch.");
    } else if (["any", "all"].includes(kind) && entry.type !== "Bool") {
      add("AIBT2045", `${pointer}/reduction`, "Boolean reducer/type mismatch.");
    }
  }

  for (const keyId of declaredSharedWrites) {
    const entry = blackboard[keyId];
    if (entry?.scope === "shared" && entry.reduction === undefined) {
      add("AIBT2044", `/blackboard/${keyId}`, "Shared write has no reduction.");
    }
  }
  return errors;
}

function scopeEntries(document, scope) {
  return Object.entries(document.blackboard)
    .filter(([, entry]) => entry.scope === scope)
    .sort(([left], [right]) => ordinalUtf8(left, right));
}

function scopeStream(document, scope) {
  const output = new Bytes();
  const descriptor = document.blackboardContracts[scope];
  const entries = scopeEntries(document, scope);
  output.text("aibt.blackboard-scope");
  output.u32(1);
  output.u8(scopeCodes[scope]);
  output.text(descriptor.contractId);
  output.u32(descriptor.contractVersion);
  output.u32(entries.length);
  for (const [keyId, entry] of entries) {
    output.text(keyId);
    output.text(entry.type);
    output.u32(entry.typeVersion);
    output.text(entry.enumContract ?? "");
    output.bytes(Buffer.from(canonicalJson(entry.default, entry.type), "utf8"));
    output.u8(reductionCodes[entry.reduction?.kind ?? "none"]);
  }
  return output.build();
}

function scopeHash(document, scope) {
  return sha256(scopeStream(document, scope));
}

function layoutStream(document, layout, scope) {
  const output = new Bytes();
  const descriptor = document.blackboardContracts[scope];
  const slots = layout[scope].slots;
  output.text("aibt.blackboard-layout");
  output.u32(1);
  output.u8(scopeCodes[scope]);
  output.text(descriptor.contractId);
  output.u32(descriptor.contractVersion);
  output.raw(Buffer.from(scopeHash(document, scope), "hex"));
  output.u32(slots.length);
  for (const slot of slots) {
    const entry = document.blackboard[slot.keyId];
    if (!entry || entry.scope !== scope) throw new Error(`layout slot ${slot.keyId} has wrong scope`);
    output.text(slot.keyId);
    output.u32(slot.slotIndex);
    output.u64(fnv1a64(entry.type));
    output.u32(entry.typeVersion);
    output.u64(entry.enumContract ? fnv1a64(entry.enumContract) : 0n);
    output.u32(slot.offset);
    output.u32(slot.size);
    output.u32(slot.alignment);
    output.bytes(Buffer.from(canonicalJson(entry.default, entry.type), "utf8"));
    output.u8(reductionCodes[entry.reduction?.kind ?? "none"]);
  }
  return output.build();
}

function layoutHash(document, layout, scope) {
  return sha256(layoutStream(document, layout, scope));
}

function encodeCompiledDefault(entry, size) {
  const output = Buffer.alloc(size);
  const value = Object.hasOwn(entry, "default")
    ? entry.default
    : entry.type === "Bool" ? false : entry.type.startsWith("FixedString") ? "" : 0;
  switch (entry.type) {
    case "Bool": output.writeUInt8(value ? 1 : 0); break;
    case "Int32": output.writeInt32LE(Number(value)); break;
    case "Int64": output.writeBigInt64LE(BigInt(value)); break;
    case "Float32": output.writeFloatLE(canonicalZero(Math.fround(value))); break;
    case "Float64": output.writeDoubleLE(canonicalZero(value)); break;
    case "Float2":
      output.writeFloatLE(canonicalZero(Math.fround(value.x)), 0);
      output.writeFloatLE(canonicalZero(Math.fround(value.y)), 4);
      break;
    case "Float3":
      output.writeFloatLE(canonicalZero(Math.fround(value.x)), 0);
      output.writeFloatLE(canonicalZero(Math.fround(value.y)), 4);
      output.writeFloatLE(canonicalZero(Math.fround(value.z)), 8);
      break;
    case "Quaternion":
      output.writeFloatLE(canonicalZero(Math.fround(value.x)), 0);
      output.writeFloatLE(canonicalZero(Math.fround(value.y)), 4);
      output.writeFloatLE(canonicalZero(Math.fround(value.z)), 8);
      output.writeFloatLE(canonicalZero(Math.fround(value.w)), 12);
      break;
    case "Enum32":
      output.writeBigUInt64LE(fnv1a64(value.contract), 0);
      output.writeInt32LE(Number(value.value), 8);
      break;
    case "FixedString32":
    case "FixedString64":
    case "FixedString128":
    case "FixedString512": {
      const bytes = Buffer.from(value, "utf8");
      if (bytes.length > size - 2) throw new Error(`${entry.type} compiled default exceeds its physical slot`);
      output.writeUInt16LE(bytes.length, 0);
      bytes.copy(output, 2);
      break;
    }
    case "AgentId":
    case "EntityId":
      output.writeBigUInt64LE(BigInt(value), 0);
      break;
    case "OperationId": {
      const [treeInstanceId, runtimeNodeIndex, activationGeneration, sequence] = value.split(":");
      output.writeBigUInt64LE(BigInt(treeInstanceId), 0);
      output.writeUInt32LE(Number(runtimeNodeIndex), 8);
      output.writeUInt32LE(Number(activationGeneration), 12);
      output.writeBigUInt64LE(BigInt(sequence), 16);
      break;
    }
    case "AssetId":
      output.writeBigUInt64LE(BigInt(`0x${value.guid.slice(0, 16)}`), 0);
      output.writeBigUInt64LE(BigInt(`0x${value.guid.slice(16)}`), 8);
      output.writeBigInt64LE(Object.hasOwn(value, "localFileId") ? BigInt(value.localFileId) : 0n, 16);
      output.writeUInt8(Object.hasOwn(value, "localFileId") ? 1 : 0, 24);
      break;
    case "example.Target":
      output.writeInt32LE(value.entity, 0);
      output.writeFloatLE(canonicalZero(Math.fround(value.weight)), 4);
      break;
    default:
      throw new Error(`compiled fixture codec missing for ${entry.type}`);
  }
  return output;
}

function scopeVariant(document, layout, scopes) {
  const keep = new Set(scopes);
  const nextDocument = clone(document);
  nextDocument.blackboard = Object.fromEntries(Object.entries(nextDocument.blackboard)
    .filter(([, entry]) => (entry.scope ?? "tree") === "tree" || keep.has(entry.scope)));
  nextDocument.blackboardContracts = Object.fromEntries(Object.entries(nextDocument.blackboardContracts)
    .filter(([scope]) => keep.has(scope)));
  const retainedKeys = new Set(Object.keys(nextDocument.blackboard));
  for (const node of Object.values(nextDocument.nodes)) {
    if (node.observer && node.observer.watchedKeys.some(key => !retainedKeys.has(key))) delete node.observer;
  }
  const nextLayout = clone(layout);
  for (const scope of ["agent", "shared"]) {
    if (!keep.has(scope)) delete nextLayout[scope];
  }
  nextLayout.accesses = nextLayout.accesses.filter(access => access.scope === "tree" || keep.has(access.scope));
  const watchedSlots = [];
  const observers = [];
  for (const observer of nextLayout.observers) {
    const source = nextLayout.watchedSlots.slice(observer.firstWatchedSlot, observer.firstWatchedSlot + observer.watchedSlotCount);
    if (source.some(watched => watched.scope !== "tree" && !keep.has(watched.scope))) continue;
    observers.push({ ...observer, firstWatchedSlot: watchedSlots.length });
    watchedSlots.push(...source);
  }
  nextLayout.observers = observers;
  nextLayout.watchedSlots = watchedSlots;
  return { document: nextDocument, layout: nextLayout };
}

function typedDefaultVariant(document, layout, values) {
  const nextDocument = clone(document);
  const entries = [
    ["codec_agent_id", "AgentId", values.agentId, undefined],
    ["codec_asset_id", "AssetId", values.assetId, undefined],
    ["codec_entity_id", "EntityId", values.entityId, undefined],
    ["codec_enum32", "Enum32", values.enum32, values.enum32.contract],
    ["codec_float2", "Float2", values.float2, undefined],
    ["codec_float3", "Float3", values.float3, undefined],
    ["codec_operation_id", "OperationId", values.operationId, undefined],
    ["codec_quaternion", "Quaternion", values.quaternion, undefined]
  ];
  for (const [keyId, type, defaultValue, enumContract] of entries) {
    nextDocument.blackboard[keyId] = {
      type,
      typeVersion: 1,
      ...(enumContract ? { enumContract } : {}),
      scope: "agent",
      default: defaultValue
    };
  }
  const nextLayout = clone(layout);
  nextLayout.agent.slots.push(
    { keyId: "codec_agent_id", slotIndex: 2, offset: 8, size: 8, alignment: 8 },
    { keyId: "codec_asset_id", slotIndex: 3, offset: 16, size: 32, alignment: 8 },
    { keyId: "codec_entity_id", slotIndex: 4, offset: 48, size: 8, alignment: 8 },
    { keyId: "codec_enum32", slotIndex: 5, offset: 56, size: 16, alignment: 8 },
    { keyId: "codec_float2", slotIndex: 6, offset: 72, size: 8, alignment: 4 },
    { keyId: "codec_float3", slotIndex: 7, offset: 80, size: 12, alignment: 4 },
    { keyId: "codec_operation_id", slotIndex: 8, offset: 96, size: 24, alignment: 8 },
    { keyId: "codec_quaternion", slotIndex: 9, offset: 120, size: 16, alignment: 4 }
  );
  return { document: nextDocument, layout: nextLayout };
}

function compiledStream(document, layout) {
  const output = new Bytes();
  const header = layout.header;
  const configBlob = Buffer.from(layout.configBlobHex, "hex");
  const scopes = ["agent", "shared"].filter(scope => document.blackboardContracts?.[scope] && layout[scope]);
  const storageScopes = ["tree", ...scopes].filter(scope => layout[scope]);
  const slots = storageScopes.flatMap(scope => layout[scope].slots.map(slot => ({ scope, ...slot })));
  const defaultParts = [];
  const defaultOffsets = new Map();
  let defaultOffset = 0;
  for (const slot of slots) {
    const bytes = encodeCompiledDefault(document.blackboard[slot.keyId], slot.size);
    defaultOffsets.set(`${slot.scope}\0${slot.keyId}`, { offset: defaultOffset, size: bytes.length });
    defaultParts.push(bytes);
    defaultOffset += bytes.length;
  }
  const defaultBlob = Buffer.concat(defaultParts);

  output.u32(header.magic);
  output.u32(2);
  output.u32(header.executionSemanticsVersion);
  output.u16(header.compilerVersion.major);
  output.u16(header.compilerVersion.minor);
  output.u16(header.compilerVersion.patch);
  output.u32(header.compilerVersion.build);
  output.raw(Buffer.from(header.semanticHash, "hex"));
  output.raw(Buffer.from(header.registryHash, "hex"));
  output.raw(Buffer.from(header.policyHash, "hex"));
  output.u32(header.policyFormatVersion);
  output.u32(header.rootNodeIndex);
  output.u32(layout.nodes.length);
  output.u32(layout.childIndices.length);
  output.u32(slots.length);
  output.u32(layout.debugIdentities.length);
  output.u32(configBlob.length);
  const instanceMemorySize = layout.nodes.reduce((maximum, node) => Math.max(maximum, node.memoryOffset + node.memorySize), 0);
  const maximumAlignment = layout.nodes.reduce((maximum, node) => Math.max(maximum, node.memoryAlignment), 1);
  output.u32(instanceMemorySize);
  output.u32(maximumAlignment);
  output.u32(header.capabilityFlags | scopes.reduce((flags, scope) => flags | (scope === "agent" ? (1 << 7) : (1 << 8)), 0));
  output.u8(header.deterministicCompatible ? 1 : 0);

  output.u32(scopes.length);
  for (const scope of scopes) {
    const descriptor = document.blackboardContracts[scope];
    const scopeSlots = layout[scope].slots;
    output.u8(scopeCodes[scope]);
    output.text(descriptor.contractId);
    output.u64(fnv1a64(descriptor.contractId));
    output.u32(descriptor.contractVersion);
    output.raw(Buffer.from(scopeHash(document, scope), "hex"));
    output.raw(Buffer.from(layoutHash(document, layout, scope), "hex"));
    output.u32(0);
    output.u32(scopeSlots.length);
  }

  const reads = layout.accesses.filter(access => access.mode === "read");
  const writes = layout.accesses.filter(access => access.mode !== "read");
  for (const node of layout.nodes) {
    output.u64(fnv1a64(node.nodeType));
    output.u32(node.nodeTypeVersion);
    output.u32(node.configOffset);
    output.u32(node.configSize);
    output.u32(node.configAlignment);
    output.u32(node.memoryOffset);
    output.u32(node.memorySize);
    output.u32(node.memoryAlignment);
    output.u8(node.memoryLifetime === "activation" ? 1 : 2);
    output.u32(node.firstChildOffset);
    output.u32(node.childCount);
    output.u32(node.nodeFlags);
    output.u32(node.debugIdentityIndex);
    output.u32(reads.length === 0 ? 0 : 0);
    output.u32(reads.length);
    output.u32(writes.length === 0 ? reads.length : reads.length);
    output.u32(writes.length);
  }
  for (const childIndex of layout.childIndices) output.u32(childIndex);

  output.u32(layout.accesses.length);
  for (const access of layout.accesses) {
    output.u32(access.nodeIndex);
    output.u32(access.accessOrdinal);
    output.u8(scopeCodes[access.scope]);
    output.u32(access.slotIndex);
    output.u8(accessModeCodes[access.mode]);
    output.u8(reductionCodes[access.reduction ?? "none"]);
  }

  output.u32(slots.length);
  for (const slot of slots) {
    const entry = document.blackboard[slot.keyId];
    const defaults = defaultOffsets.get(`${slot.scope}\0${slot.keyId}`);
    const accessFlags = layout.accesses.reduce((flags, access) => {
      if (access.scope !== slot.scope || access.slotIndex !== slot.slotIndex) return flags;
      return flags | (access.mode === "read" ? 1 : access.mode === "write" ? 2 : 3);
    }, 0);
    output.text(slot.keyId);
    output.u64(fnv1a64(slot.keyId));
    output.u64(fnv1a64(entry.type));
    output.u32(entry.typeVersion ?? 1);
    output.u64(entry.enumContract ? fnv1a64(entry.enumContract) : 0n);
    output.u8(scopeCodes[slot.scope]);
    output.u32(slot.slotIndex);
    output.u32(slot.offset);
    output.u32(slot.size);
    output.u32(slot.alignment);
    output.u32(defaults.offset);
    output.u32(defaults.size);
    output.u8(accessFlags);
    output.u32(0xffffffff);
    output.u32(0);
  }
  output.u32(layout.observers.length);
  for (const observer of layout.observers) {
    output.u32(observer.observerNodeIndex);
    output.u32(observer.owningReactiveCompositeIndex);
    output.u8(observerModeCodes[observer.mode]);
    output.u32(observer.firstWatchedSlot);
    output.u32(observer.watchedSlotCount);
  }
  output.u32(layout.watchedSlots.length);
  for (const watched of layout.watchedSlots) {
    output.u8(scopeCodes[watched.scope]);
    output.u32(watched.slotIndex);
  }
  output.bytes(configBlob);
  output.bytes(defaultBlob);
  output.u32(scopes.length);
  for (const scope of scopes) output.bytes(layoutStream(document, layout, scope));
  output.u32(layout.debugIdentities.length);
  for (const debug of layout.debugIdentities) {
    output.u32(debug.runtimeNodeIndex);
    output.text(debug.nodeId);
    output.text(debug.sourcePath);
    output.text(debug.displayName ?? "");
  }
  return output.build();
}

function compiledHash(document, layout) {
  return sha256(compiledStream(document, layout));
}

function contractSetErrors(inputs) {
  const seen = new Map();
  const errors = [];
  for (const input of inputs) {
    const document = input.document ?? input;
    const layout = input.layout;
    for (const scope of ["agent", "shared"]) {
      if (!document.blackboardContracts?.[scope]) continue;
      const descriptor = document.blackboardContracts[scope];
      const key = `${scope}\0${descriptor.contractId}\0${descriptor.contractVersion}`;
      const value = {
        schema: scopeHash(document, scope),
        layout: layout?.[scope] ? layoutHash(document, layout, scope) : null
      };
      const previous = seen.get(key);
      if (previous && (previous.schema !== value.schema
        || previous.layout !== null && value.layout !== null && previous.layout !== value.layout)) {
        errors.push({ code: "AIBT2043", pointer: `/blackboardContracts/${scope}` });
      } else {
        seen.set(key, value);
      }
    }
  }
  return errors;
}

function canonicalZero(value) {
  return Object.is(value, -0) ? 0 : value;
}

function reductionValueError(type, typeVersion, value, enumContract = undefined) {
  return validateTypedValue({ type, typeVersion, enumContract }, value);
}

function compareContribution(left, right) {
  const tree = BigInt(left.treeInstanceId) - BigInt(right.treeInstanceId);
  if (tree < 0n) return -1;
  if (tree > 0n) return 1;
  const sequence = BigInt(left.sequence) - BigInt(right.sequence);
  return sequence < 0n ? -1 : sequence > 0n ? 1 : 0;
}

function reduceOne(kind, type, source, typeContract = {}) {
  if (source.length === 0) return { ok: true, hasValue: false };
  const contributions = [...source].sort(compareContribution);
  for (let index = 1; index < contributions.length; index++) {
    if (compareContribution(contributions[index - 1], contributions[index]) === 0) {
      return { ok: false, error: "duplicate-key" };
    }
  }
  const typeVersion = typeContract.typeVersion ?? (type === "example.Target" ? 2 : 1);
  if (contributions.some(item => reductionValueError(type, typeVersion, item.value, typeContract.enumContract) !== null)) {
    return { ok: false, error: "value" };
  }
  if (kind === "first") return { ok: true, hasValue: true, value: clone(contributions[0].value) };
  if (kind === "last") return { ok: true, hasValue: true, value: clone(contributions.at(-1).value) };

  if (["any", "all"].includes(kind)) {
    if (type !== "Bool" || contributions.some(item => typeof item.value !== "boolean")) return { ok: false, error: "type" };
    let value = contributions[0].value;
    for (let index = 1; index < contributions.length; index++) {
      value = kind === "any" ? value || contributions[index].value : value && contributions[index].value;
    }
    return { ok: true, hasValue: true, value };
  }

  if (!numericTypes.has(type)) return { ok: false, error: "type" };
  if (type === "Int32" || type === "Int64") {
    const minimum = type === "Int32" ? -2147483648n : -9223372036854775808n;
    const maximum = type === "Int32" ? 2147483647n : 9223372036854775807n;
    let value = BigInt(contributions[0].value);
    if (value < minimum || value > maximum) return { ok: false, error: "overflow" };
    for (let index = 1; index < contributions.length; index++) {
      const next = BigInt(contributions[index].value);
      if (next < minimum || next > maximum) return { ok: false, error: "overflow" };
      if (kind === "sum") value += next;
      else if (kind === "min" && next < value) value = next;
      else if (kind === "max" && next > value) value = next;
      if (value < minimum || value > maximum) return { ok: false, error: "overflow" };
    }
    return { ok: true, hasValue: true, value: type === "Int64" ? value : Number(value) };
  }

  const round = type === "Float32" ? Math.fround : value => value;
  let value = canonicalZero(round(contributions[0].value));
  if (!Number.isFinite(value)) return { ok: false, error: "non-finite" };
  for (let index = 1; index < contributions.length; index++) {
    const next = canonicalZero(round(contributions[index].value));
    if (!Number.isFinite(next)) return { ok: false, error: "non-finite" };
    if (kind === "sum") value = canonicalZero(round(value + next));
    else if (kind === "min" && next < value) value = next;
    else if (kind === "max" && next > value) value = next;
    if (!Number.isFinite(value)) return { ok: false, error: "non-finite" };
  }
  return { ok: true, hasValue: true, value };
}

function canonicalEqual(left, right) {
  if (typeof left === "bigint" || typeof right === "bigint") {
    try { return BigInt(left) === BigInt(right); } catch { return false; }
  }
  if (typeof left === "number" && typeof right === "number") {
    if (!Number.isFinite(left) || !Number.isFinite(right)) return false;
    return canonicalZero(left) === canonicalZero(right);
  }
  return canonicalJson(left) === canonicalJson(right);
}

function reduceContext(state, slotContracts, streams, equalities = {}) {
  const contributions = [];
  const semanticKeys = new Set();
  for (const stream of streams) {
    const ownerTreeId = parseNonzeroU64(stream?.treeInstanceId);
    if (!stream || stream.valid !== true || !Array.isArray(stream.entries)
      || !Number.isInteger(stream.capacity) || stream.capacity < 1
      || stream.capacity > 0xffffffff || stream.entries.length > stream.capacity
      || ownerTreeId === null
      || String(ownerTreeId) !== String(stream.treeInstanceId)
      || String(stream.entries.length) !== String(stream.nextSequence)) {
      return { ok: false, state: clone(state), error: "invalid-stream" };
    }
    for (let recordIndex = 0; recordIndex < stream.entries.length; recordIndex++) {
      const record = stream.entries[recordIndex];
      const slot = slotContracts.find(candidate => candidate.slotIndex === record.slotIndex);
      const typeId = slot?.typeId ?? slot?.type;
      const typeVersion = slot?.typeVersion ?? (typeId === "example.Target" ? 2 : 1);
      const recordTreeId = parseNonzeroU64(record.treeInstanceId);
      const sequence = parseCanonicalUnsigned(record.sequence, maxU64 - 1n, false);
      if (recordTreeId === null || sequence === null) {
        return { ok: false, state: clone(state), error: "malformed-contribution" };
      }
      const semanticKey = `${recordTreeId}\0${sequence}`;
      if (!slot || recordTreeId !== ownerTreeId
        || record.capacity !== stream.capacity
        || record.typeId !== typeId
        || record.typeVersion !== typeVersion
        || String(recordTreeId) !== String(record.treeInstanceId)
        || String(sequence) !== String(record.sequence)
        || sequence !== BigInt(recordIndex)
        || semanticKeys.has(semanticKey)
        || reductionValueError(typeId, typeVersion, record.value, slot.enumContract) !== null) {
        return { ok: false, state: clone(state), error: "malformed-contribution" };
      }
      semanticKeys.add(semanticKey);
      contributions.push(record);
    }
  }
  const staged = clone(state);
  let changed = false;
  for (const slot of [...slotContracts].sort((left, right) => left.slotIndex - right.slotIndex)) {
    const source = contributions.filter(item => item.slotIndex === slot.slotIndex);
    const slotType = slot.typeId ?? slot.type;
    const slotTypeVersion = slot.typeVersion ?? (slotType === "example.Target" ? 2 : 1);
    const result = reduceOne(slot.kind, slotType, source, {
      typeVersion: slotTypeVersion,
      enumContract: slot.enumContract
    });
    if (!result.ok) return { ok: false, state: clone(state), error: result.error };
    if (!result.hasValue) continue;
    let isEqual;
    try {
      const equality = equalities[slot.slotIndex];
      if (registeredSchemas[slotType] && typeof equality !== "function") {
        return { ok: false, state: clone(state), error: "equality-failure" };
      }
      isEqual = equality
        ? equality(staged.values[slot.slotIndex], result.value)
        : canonicalEqual(staged.values[slot.slotIndex], result.value);
    } catch {
      return { ok: false, state: clone(state), error: "equality-failure" };
    }
    if (typeof isEqual !== "boolean") {
      return { ok: false, state: clone(state), error: "equality-failure" };
    }
    if (isEqual) continue;
    const version = BigInt(staged.versions[slot.slotIndex]);
    if (version === maxU64) return { ok: false, state: clone(state), error: "version-overflow" };
    staged.values[slot.slotIndex] = result.value;
    staged.versions[slot.slotIndex] = (version + 1n).toString();
    changed = true;
  }
  if (changed) {
    const revision = BigInt(staged.revision);
    if (revision === maxU64) return { ok: false, state: clone(state), error: "revision-overflow" };
    staged.revision = (revision + 1n).toString();
  }
  return { ok: true, state: staged, changed };
}

function permutations(values) {
  if (values.length < 2) return [values];
  const result = [];
  for (let index = 0; index < values.length; index++) {
    const rest = values.slice(0, index).concat(values.slice(index + 1));
    for (const suffix of permutations(rest)) result.push([values[index], ...suffix]);
  }
  return result;
}

function createContributionStream(treeInstanceId, capacity) {
  const parsedTreeId = parseNonzeroU64(treeInstanceId);
  return {
    treeInstanceId: parsedTreeId === null ? String(treeInstanceId) : parsedTreeId.toString(),
    capacity,
    nextSequence: "0",
    valid: Number.isInteger(capacity) && capacity >= 1 && capacity <= 0xffffffff && parsedTreeId !== null,
    entries: []
  };
}

function appendContribution(stream, slot, value) {
  if (!stream.valid) return false;
  if (stream.entries.length >= stream.capacity || BigInt(stream.nextSequence) >= maxU64) {
    stream.valid = false;
    stream.entries = [];
    return false;
  }
  const typeId = slot.typeId ?? slot.type;
  const typeVersion = slot.typeVersion ?? (typeId === "example.Target" ? 2 : 1);
  if (reductionValueError(typeId, typeVersion, value, slot.enumContract) !== null) {
    stream.valid = false;
    stream.entries = [];
    return false;
  }
  stream.entries.push({
    slotIndex: slot.slotIndex,
    treeInstanceId: stream.treeInstanceId,
    sequence: stream.nextSequence,
    typeId,
    typeVersion,
    capacity: stream.capacity,
    value: clone(value)
  });
  stream.nextSequence = (BigInt(stream.nextSequence) + 1n).toString();
  return true;
}

function createAgentState(document) {
  const entries = scopeEntries(document, "agent");
  return {
    values: Object.fromEntries(entries.map(([keyId, entry]) => [keyId, clone(entry.default)])),
    versions: Object.fromEntries(entries.map(([keyId]) => [keyId, "0"])),
    revision: "0"
  };
}

function resetAgentState(state, document, equalities = {}) {
  const staged = clone(state);
  let changed = false;
  for (const [keyId, entry] of scopeEntries(document, "agent")) {
    let isEqual;
    try {
      isEqual = equalities[keyId]
        ? equalities[keyId](staged.values[keyId], entry.default)
        : canonicalEqual(staged.values[keyId], entry.default);
    } catch {
      return { ok: false, state: clone(state), error: "equality-failure" };
    }
    if (isEqual) continue;
    const version = BigInt(staged.versions[keyId]);
    if (version === maxU64) return { ok: false, state: clone(state) };
    staged.values[keyId] = clone(entry.default);
    staged.versions[keyId] = (version + 1n).toString();
    changed = true;
  }
  if (changed) {
    const revision = BigInt(staged.revision);
    if (revision === maxU64) return { ok: false, state: clone(state) };
    staged.revision = (revision + 1n).toString();
  }
  return { ok: true, changed, state: staged };
}

function agentDescriptor(document, layout) {
  const scope = "agent";
  const contract = document.blackboardContracts[scope];
  const access = layout.accesses
    .filter(item => item.scope === scope)
    .map(item => {
      const slot = layout.agent.slots.find(candidate => candidate.slotIndex === item.slotIndex);
      const entry = document.blackboard[slot.keyId];
      return {
        ordinal: item.accessOrdinal,
        slotIndex: item.slotIndex,
        mode: item.mode,
        typeId: entry.type,
        typeVersion: entry.typeVersion
      };
    });
  return {
    contractId: contract.contractId,
    contractNumericId: fnv1a64(contract.contractId).toString(),
    contractVersion: contract.contractVersion,
    schemaHash: scopeHash(document, scope),
    layoutHash: layoutHash(document, layout, scope),
    slots: layout.agent.slots.map(slot => {
      const entry = document.blackboard[slot.keyId];
      return { slotIndex: slot.slotIndex, typeId: entry.type, typeVersion: entry.typeVersion };
    }),
    access
  };
}

function descriptorEqual(left, right) {
  return left.contractId === right.contractId
    && left.contractNumericId === right.contractNumericId
    && left.contractVersion === right.contractVersion
    && left.schemaHash === right.schemaHash
    && left.layoutHash === right.layoutHash
    && canonicalJson(left.slots) === canonicalJson(right.slots);
}

function accessCompatible(contextDescriptor, bindingDescriptor) {
  const ordinals = new Set();
  return bindingDescriptor.access.every(access => {
    const slot = contextDescriptor.slots.find(candidate => candidate.slotIndex === access.slotIndex);
    if (!slot || ordinals.has(access.ordinal) || !["read", "write", "readwrite"].includes(access.mode)
      || slot.typeId !== access.typeId || slot.typeVersion !== access.typeVersion) return false;
    ordinals.add(access.ordinal);
    return true;
  });
}

class AgentContextModel {
  constructor(agentId, descriptor, state) {
    this.agentId = BigInt(agentId);
    this.descriptor = clone(descriptor);
    this.state = clone(state);
    this.bindings = new Map();
    this.leaseOwner = null;
    this.leaseOrder = [];
    this.leaseCursor = 0;
    this.disposed = false;
  }

  bind(treeInstanceId, descriptor) {
    const parsed = parseNonzeroU64(treeInstanceId);
    if (parsed === null) return false;
    const id = parsed.toString();
    if (this.disposed || this.leaseOwner !== null || this.leaseOrder.length !== 0 || this.bindings.has(id)
      || !descriptorEqual(this.descriptor, descriptor) || !accessCompatible(this.descriptor, descriptor)) return false;
    this.bindings.set(id, clone(descriptor));
    return true;
  }

  rebind(treeInstanceId, descriptor) {
    const parsed = parseNonzeroU64(treeInstanceId);
    if (parsed === null) return false;
    const id = parsed.toString();
    if (this.bindings.has(id)) return false;
    return this.bind(treeInstanceId, descriptor);
  }

  unbind(treeInstanceId) {
    const parsed = parseNonzeroU64(treeInstanceId);
    if (parsed === null) return false;
    const id = parsed.toString();
    if (this.disposed || this.leaseOwner !== null || this.leaseOrder.length !== 0) return false;
    return this.bindings.delete(id);
  }

  beginEligiblePass(treeInstanceIds) {
    if (this.disposed || this.leaseOwner !== null || this.leaseOrder.length !== 0) return false;
    const order = [...treeInstanceIds].map(value => parseNonzeroU64(value));
    if (order.some(value => value === null)) return false;
    if (new Set(order.map(value => value.toString())).size !== order.length
      || order.some(value => !this.bindings.has(value.toString()))) return false;
    order.sort((left, right) => left < right ? -1 : left > right ? 1 : 0);
    this.leaseOrder = order;
    this.leaseCursor = 0;
    return true;
  }

  tryExecute(treeInstanceId, callback) {
    const id = parseNonzeroU64(treeInstanceId);
    if (id === null) return false;
    if (this.disposed || this.leaseOwner !== null || this.leaseCursor >= this.leaseOrder.length
      || this.leaseOrder[this.leaseCursor] !== id) return false;
    this.leaseOwner = id;
    callback();
    return true;
  }

  release(treeInstanceId) {
    const id = parseNonzeroU64(treeInstanceId);
    if (id === null) return false;
    if (this.leaseOwner !== id) return false;
    this.leaseOwner = null;
    this.leaseCursor++;
    if (this.leaseCursor === this.leaseOrder.length) {
      this.leaseOrder = [];
      this.leaseCursor = 0;
    }
    return true;
  }

  reset(document, equalities = {}) {
    if (this.disposed || this.leaseOwner !== null || this.leaseOrder.length !== 0) return { ok: false, state: clone(this.state) };
    const result = resetAgentState(this.state, document, equalities);
    if (result.ok) this.state = clone(result.state);
    return result;
  }

  dispose() {
    if (this.disposed || this.leaseOwner !== null || this.leaseOrder.length !== 0 || this.bindings.size !== 0) return false;
    this.disposed = true;
    return true;
  }
}

class AgentContextRegistryModel {
  constructor() { this.contexts = new Map(); }
  create(agentId, descriptor, state) {
    const id = parseNonzeroU64(agentId);
    if (id === null || this.contexts.has(id.toString())) return null;
    const context = new AgentContextModel(id, descriptor, state);
    this.contexts.set(id.toString(), context);
    return context;
  }
  destroy(agentId) {
    const parsed = parseNonzeroU64(agentId);
    if (parsed === null) return false;
    const id = parsed.toString();
    const context = this.contexts.get(id);
    if (!context?.disposed) return false;
    return this.contexts.delete(id);
  }
}

function run() {
  const schema = JSON.parse(fs.readFileSync(path.join(here, "tree-v2.schema.json"), "utf8"));
  const valid = fixture("tree-v2.valid.aibt.json");
  const reordered = fixture("tree-v2.reordered.input.json");
  const treeOnly = fixture("tree-v2.tree-only-v1-shape.json");
  const invalidCustom = fixture("tree-v2.invalid-custom.json");
  const invalidAgentRequired = fixture("tree-v2.invalid-agent-required.json");
  const layout = fixture("compiled-layout-v2.json");
  const vectors = fixture("reduction-vectors.json").vectors;
  const losslessInt64 = losslessIntegerFixture("lossless-int64-defaults.json");
  const float32Oracle = fixture("float32-canonical-vectors.json").vectors;
  const compiledDefaultValues = losslessIntegerFixture("compiled-default-values.json");

  if (process.argv.includes("--print-float32")) {
    process.stdout.write(`${JSON.stringify(Object.fromEntries(float32Oracle.map(vector => [
      vector.bitsHex,
      canonicalFloat32(float32FromBits(vector.bitsHex))
    ])), null, 2)}\n`);
    return;
  }

  equal(schema.$schema, "https://json-schema.org/draft/2020-12/schema", "schema draft is pinned");
  equal(schema.properties.formatVersion.const, 2, "schema format version is pinned");
  check(!schema.$defs.blackboardEntry.properties.reduction.properties.kind.enum.includes("custom"), "schema rejects custom reducers");
  equal(validateTree(valid, ["shared_sum_score"]), [], "valid schema/model fixture");
  equal(validateTree(reordered, ["shared_sum_score"]), [], "reordered semantic input remains valid");
  equal(validateTree(treeOnly), [], "v2 remains additive-complete for a v1-shaped Tree-only document");
  equal(validateTree(invalidCustom)[0].code, "AIBT2046", "custom reducer has stable compilation code");
  check(validateTree(invalidAgentRequired).some(error => error.code === "AIBT1004"),
    "Agent typeVersion/default requirements are conditional and enforced");
  equal(validateTree(valid, ["shared_read_only"])[0].code, "AIBT2044", "unconfigured Shared write has stable compilation code");

  const missingContract = clone(valid);
  delete missingContract.blackboardContracts.agent;
  equal(validateTree(missingContract)[0].code, "AIBT2042", "missing Agent contract has stable compilation code");
  const wrongReduction = clone(valid);
  wrongReduction.blackboard.shared_any_alert.type = "Int32";
  check(validateTree(wrongReduction).some(error => error.code === "AIBT2045"), "incompatible reducer has stable compilation code");

  const typedRange = clone(valid);
  typedRange.blackboard.agent_health.default = 2147483648;
  check(validateTree(typedRange).some(error => error.code === "AIBT2007"), "Int32 default range is validated");
  const typedRegisteredMissing = clone(valid);
  delete typedRegisteredMissing.blackboard.shared_last_target.default.weight;
  check(validateTree(typedRegisteredMissing).some(error => error.code === "AIBT2007"), "registered default requires every schema member");
  const typedRegisteredExtra = clone(valid);
  typedRegisteredExtra.blackboard.shared_last_target.default.extra = 1;
  check(validateTree(typedRegisteredExtra).some(error => error.code === "AIBT2007"), "registered default rejects unknown members");
  const typedRegisteredVersion = clone(valid);
  typedRegisteredVersion.blackboard.shared_last_target.typeVersion = 3;
  check(validateTree(typedRegisteredVersion).some(error => error.code === "AIBT2007"), "registered default requires exact type version");
  const enumTree = clone(valid);
  enumTree.blackboard.agent_mode = {
    type: "Enum32", typeVersion: 1, enumContract: "aibt.enum.mode", scope: "agent",
    default: { contract: "aibt.enum.mode", value: 2 }
  };
  equal(validateTree(enumTree), [], "matching Enum32 contract/default is valid");
  enumTree.blackboard.agent_mode.default.contract = "aibt.enum.other";
  check(validateTree(enumTree).some(error => error.code === "AIBT2007"), "Enum32 default contract mismatch is rejected");
  const fixedStringRange = clone(valid);
  fixedStringRange.blackboard.shared_first_label.default = "x".repeat(33);
  check(validateTree(fixedStringRange).some(error => error.code === "AIBT2007"), "fixed-string UTF-8 capacity is validated");
  equal(validateTypedValue({ type: "Int64", typeVersion: 1 }, 9223372036854775807n), null,
    "Int64 maximum default validates exactly");
  equal(validateTypedValue({ type: "Int64", typeVersion: 1 }, -9223372036854775808n), null,
    "Int64 minimum default validates exactly");
  check(validateTypedValue({ type: "Int64", typeVersion: 1 }, 9223372036854775808n) !== null,
    "Int64 out-of-range default is rejected exactly");
  check(validateTypedValue({ type: "Float3", typeVersion: 1 }, { x: 0, y: Number.NaN, z: 0 }) !== null,
    "non-finite vector component is rejected");
  check(validateTypedValue({ type: "Quaternion", typeVersion: 1 }, { x: 0, y: 0, z: 0, w: 1e39 }) !== null,
    "Quaternion component must be finite after Float32 conversion");
  equal(canonicalJson({ w: 4, z: 3, y: 2, x: 1 }, "Quaternion"), '{"x":1,"y":2,"z":3,"w":4}',
    "Quaternion canonical member order is exactly x,y,z,w");
  equal(canonicalJson(0.1, "Float32"), "0.1", "Float32 uses shortest declared-precision round-trip text");
  check(Object.is(Math.fround(Number(canonicalJson(Math.fround(1.234567), "Float32"))), Math.fround(1.234567)),
    "Float32 canonical text round-trips in Float32 precision");
  for (const vector of float32Oracle) {
    const value = float32FromBits(vector.bitsHex);
    const actual = canonicalFloat32(value);
    equal(actual, vector.expected, `independent Float32 oracle: ${vector.case}`);
    const expectedRoundTrip = Object.is(Math.fround(value), -0) ? 0 : Math.fround(value);
    check(Object.is(Math.fround(Number(actual)), expectedRoundTrip), `Float32 oracle round-trip: ${vector.case}`);
  }
  equal(validateTypedValue({ type: "AgentId", typeVersion: 1 }, maxU64.toString()), null,
    "AgentId accepts canonical nonzero UInt64 maximum");
  check(validateTypedValue({ type: "AgentId", typeVersion: 1 }, "0") !== null,
    "AgentId rejects zero");
  check(validateTypedValue({ type: "EntityId", typeVersion: 1 }, (maxU64 + 1n).toString()) !== null,
    "EntityId rejects UInt64 overflow");
  equal(validateTypedValue({ type: "OperationId", typeVersion: 1 }, `1:0:0:${maxU64}`), null,
    "OperationId accepts the four-field runtime grammar");
  check(validateTypedValue({ type: "OperationId", typeVersion: 1 }, "1:4294967295:0:0") !== null,
    "OperationId rejects the invalid runtime-node sentinel");

  const agentOnly = scopeVariant(valid, layout, ["agent"]);
  const sharedOnly = scopeVariant(valid, layout, ["shared"]);
  check(agentOnly.document.nodes.root.observer === undefined
    && agentOnly.layout.observers.length === 0 && agentOnly.layout.watchedSlots.length === 0,
    "Agent-only fixture removes observers that depend on removed Shared keys");
  check(sharedOnly.document.nodes.root.observer !== undefined
    && sharedOnly.layout.observers.length === 1 && sharedOnly.layout.watchedSlots.length === 1,
    "Shared-only fixture retains its valid observer dependency");
  const losslessDocument = clone(valid);
  losslessDocument.blackboard.agent_health.type = "Int64";
  losslessDocument.blackboard.agent_health.default = losslessInt64.maximum;
  const losslessLayout = clone(layout);
  losslessLayout.agent.slots[0].size = 8;
  losslessLayout.agent.slots[0].alignment = 8;
  losslessLayout.agent.slots[1].offset = 8;
  const typedDefaults = typedDefaultVariant(valid, layout, compiledDefaultValues);
  equal(validateTree(typedDefaults.document), [], "all fieldwise compiled-default fixture types validate");
  const exactCompiledDefaults = {
    Float2: "0000c03f00000000",
    Float3: "000010c0bd3786350000804b",
    Quaternion: "0000803e000000bf0000803f000080bf",
    Enum32: "91f7287098f5e3c0f9ffffff00000000",
    AgentId: "ffffffffffffffff",
    EntityId: "0000000000000080",
    OperationId: "fffffffffffffffffeffffffffffffffffffffffffffffff",
    AssetId: "efcdab89674523011032547698badcfe00000000000000800100000000000000"
  };
  const typedDefaultsByType = Object.fromEntries(Object.values(typedDefaults.document.blackboard)
    .filter(entry => Object.hasOwn(exactCompiledDefaults, entry.type))
    .map(entry => [entry.type, entry]));
  const compiledSizes = { Float2: 8, Float3: 12, Quaternion: 16, Enum32: 16, AgentId: 8, EntityId: 8, OperationId: 24, AssetId: 32 };
  for (const [type, expectedHex] of Object.entries(exactCompiledDefaults)) {
    equal(encodeCompiledDefault(typedDefaultsByType[type], compiledSizes[type]).toString("hex"), expectedHex,
      `${type} compiled default uses exact fieldwise bytes`);
  }
  const hashes = {
    agentSchema: scopeHash(valid, "agent"),
    sharedSchema: scopeHash(valid, "shared"),
    agentLayout: layoutHash(valid, layout, "agent"),
    sharedLayout: layoutHash(valid, layout, "shared"),
    compiledContent: compiledHash(valid, layout),
    compiledAgentOnly: compiledHash(agentOnly.document, agentOnly.layout),
    compiledSharedOnly: compiledHash(sharedOnly.document, sharedOnly.layout),
    losslessInt64AgentSchema: scopeHash(losslessDocument, "agent"),
    losslessInt64Compiled: compiledHash(losslessDocument, losslessLayout),
    typedDefaultsCompiled: compiledHash(typedDefaults.document, typedDefaults.layout)
  };
  if (process.argv.includes("--print-streams")) {
    process.stdout.write(`${JSON.stringify({
      agentSchemaBytes: scopeStream(valid, "agent").toString("hex"),
      sharedSchemaBytes: scopeStream(valid, "shared").toString("hex"),
      agentLayoutBytes: layoutStream(valid, layout, "agent").toString("hex"),
      sharedLayoutBytes: layoutStream(valid, layout, "shared").toString("hex"),
      compiledContentBytes: compiledStream(valid, layout).toString("hex"),
      compiledAgentOnlyBytes: compiledStream(agentOnly.document, agentOnly.layout).toString("hex"),
      compiledSharedOnlyBytes: compiledStream(sharedOnly.document, sharedOnly.layout).toString("hex"),
      losslessInt64AgentSchemaBytes: scopeStream(losslessDocument, "agent").toString("hex"),
      losslessInt64CompiledBytes: compiledStream(losslessDocument, losslessLayout).toString("hex"),
      typedDefaultsCompiledBytes: compiledStream(typedDefaults.document, typedDefaults.layout).toString("hex")
    }, null, 2)}\n`);
    return;
  }
  equal(scopeHash(reordered, "agent"), hashes.agentSchema, "property order and descriptions do not affect Agent schema hash");
  equal(scopeHash(reordered, "shared"), hashes.sharedSchema, "property order does not affect Shared schema hash");
  equal(compiledHash(reordered, layout), hashes.compiledContent, "property order does not affect compiled content hash");

  const defaultMutation = clone(valid);
  defaultMutation.blackboard.agent_health.default = 99;
  check(scopeHash(defaultMutation, "agent") !== hashes.agentSchema, "default changes Agent schema hash");
  check(layoutHash(defaultMutation, layout, "agent") !== hashes.agentLayout, "default changes Agent layout hash");
  check(compiledHash(defaultMutation, layout) !== hashes.compiledContent, "default changes compiled hash");
  const reducerMutation = clone(valid);
  reducerMutation.blackboard.shared_min_distance.reduction.kind = "max";
  check(scopeHash(reducerMutation, "shared") !== hashes.sharedSchema, "reducer changes Shared schema hash");
  check(compiledHash(reducerMutation, layout) !== hashes.compiledContent, "reducer changes compiled hash");
  const versionMutation = clone(valid);
  versionMutation.blackboardContracts.agent.contractVersion++;
  check(scopeHash(versionMutation, "agent") !== hashes.agentSchema, "contract version changes schema hash");
  const layoutMutation = clone(layout);
  layoutMutation.shared.slots.at(-1).offset += 8;
  equal(scopeHash(valid, "shared"), hashes.sharedSchema, "physical layout does not affect schema hash");
  check(layoutHash(valid, layoutMutation, "shared") !== hashes.sharedLayout, "offset changes layout hash");
  check(compiledHash(valid, layoutMutation) !== hashes.compiledContent, "offset changes compiled hash");
  const accessMutation = clone(layout);
  accessMutation.accesses[1].slotIndex = 6;
  check(compiledHash(valid, accessMutation) !== hashes.compiledContent, "access binding changes compiled hash");
  const headerMutation = clone(layout);
  headerMutation.header.semanticHash = "44".repeat(32);
  check(compiledHash(valid, headerMutation) !== hashes.compiledContent, "v1 semantic header changes compiled hash");
  const nodeMutation = clone(layout);
  nodeMutation.nodes[0].nodeFlags ^= 2;
  check(compiledHash(valid, nodeMutation) !== hashes.compiledContent, "node record changes compiled hash");
  const childMutation = clone(layout);
  childMutation.childIndices.push(0);
  childMutation.nodes[0].childCount = 1;
  check(compiledHash(valid, childMutation) !== hashes.compiledContent, "child-index stream changes compiled hash");
  const configMutation = clone(layout);
  configMutation.configBlobHex = "02000000";
  check(compiledHash(valid, configMutation) !== hashes.compiledContent, "config blob changes compiled hash");
  const debugMutation = clone(layout);
  debugMutation.debugIdentities[0].nodeId = "changed";
  check(compiledHash(valid, debugMutation) !== hashes.compiledContent, "debug identity changes compiled hash");
  const debugIndexMutation = clone(layout);
  debugIndexMutation.debugIdentities[0].runtimeNodeIndex = 1;
  check(compiledHash(valid, debugIndexMutation) !== hashes.compiledContent, "debug runtimeNodeIndex changes compiled hash");
  const observerMutation = clone(layout);
  observerMutation.observers[0].mode = "both";
  check(compiledHash(valid, observerMutation) !== hashes.compiledContent, "observer record changes compiled hash");
  const watchedSlotMutation = clone(layout);
  watchedSlotMutation.watchedSlots[0].slotIndex = 5;
  check(compiledHash(valid, watchedSlotMutation) !== hashes.compiledContent, "watched-slot table changes compiled hash");
  const treeRecordMutation = clone(layout);
  treeRecordMutation.tree.slots[0].alignment = 8;
  check(compiledHash(valid, treeRecordMutation) !== hashes.compiledContent, "v1 Tree blackboard record changes compiled hash");
  const treeDefaultMutation = clone(valid);
  treeDefaultMutation.blackboard.tree_counter.default = 1;
  check(compiledHash(treeDefaultMutation, layout) !== hashes.compiledContent, "v1 Tree default blob changes compiled hash");
  check(hashes.compiledAgentOnly !== hashes.compiledContent, "Agent-only compiled v2 stream is supported and distinct");
  check(hashes.compiledSharedOnly !== hashes.compiledContent, "Shared-only compiled v2 stream is supported and distinct");
  check(hashes.compiledAgentOnly !== hashes.compiledSharedOnly, "single-scope compiled streams retain explicit scope identity");
  check(scopeStream(losslessDocument, "agent").includes(Buffer.from("9223372036854775807", "utf8")),
    "full-width Int64 survives JSON parsing into canonical hash bytes without Number loss");
  check(compiledStream(losslessDocument, losslessLayout).includes(Buffer.from("ffffffffffffff7f", "hex")),
    "full-width Int64 survives the compiled default-blob path exactly");
  const typedDefaultsCompiledBytes = compiledStream(typedDefaults.document, typedDefaults.layout);
  for (const [type, expectedHex] of Object.entries(exactCompiledDefaults)) {
    check(typedDefaultsCompiledBytes.includes(Buffer.from(expectedHex, "hex")),
      `${type} exact fieldwise default is present in the pinned compiled stream`);
  }
  const typedDefaultMutation = clone(typedDefaults.document);
  typedDefaultMutation.blackboard.codec_quaternion.default.w = 0.5;
  check(compiledHash(typedDefaultMutation, typedDefaults.layout) !== hashes.typedDefaultsCompiled,
    "fieldwise typed-default mutation changes the compiled hash");

  const mismatch = clone(valid);
  mismatch.blackboard.agent_health.default = 99;
  equal(contractSetErrors([{ document: valid, layout }, { document: mismatch, layout }])[0].code,
    "AIBT2043", "same identity/version with changed schema is rejected");
  const mismatchLayout = clone(layout);
  mismatchLayout.agent.slots[0].offset += 8;
  equal(contractSetErrors([{ document: valid, layout }, { document: valid, layout: mismatchLayout }])[0].code,
    "AIBT2043", "same identity/version with changed physical layout is rejected");
  equal(contractSetErrors([{ document: valid, layout }, { document: reordered, layout }]), [], "identical semantic contracts compose");

  const pinned = fixture("expected-hashes.json");
  if (Object.keys(pinned).length === 0) {
    process.stdout.write(`${JSON.stringify(hashes, null, 2)}\n`);
    process.exitCode = 2;
    return;
  }
  equal(hashes, pinned, "canonical and compiled hashes match independent pins");
  const pinnedBytes = fixture("canonical-byte-streams.json");
  equal({
    agentSchemaBytes: scopeStream(valid, "agent").toString("hex"),
    sharedSchemaBytes: scopeStream(valid, "shared").toString("hex"),
    agentLayoutBytes: layoutStream(valid, layout, "agent").toString("hex"),
    sharedLayoutBytes: layoutStream(valid, layout, "shared").toString("hex")
  }, pinnedBytes, "canonical byte streams match independent pins");

  for (const vector of vectors) {
    const contributions = vector.values.map((value, index) => ({
      treeInstanceId: index + 1,
      sequence: 0,
      value
    }));
    const expected = vector.type === "Int64" ? BigInt(vector.expected) : vector.expected;
    for (const order of permutations(contributions)) {
      const result = reduceOne(vector.kind, vector.type, order);
      check(result.ok, `${vector.name} accepts canonical values`);
      equal(result.value, expected, `${vector.name} is insertion-order invariant`);
    }
    const partitions = [
      [contributions.slice(0, 1), contributions.slice(1)],
      [contributions.slice(0, 2), contributions.slice(2)],
      [contributions.filter((_, index) => index % 2 === 0), contributions.filter((_, index) => index % 2 === 1)]
    ];
    for (const partition of partitions) {
      const completionOrder = [...partition].reverse().flat();
      equal(reduceOne(vector.kind, vector.type, completionOrder).value, expected,
        `${vector.name} is batch-partition and completion-order invariant`);
    }
  }

  const keyed = [
    { treeInstanceId: 2, sequence: 0, value: "tree-2" },
    { treeInstanceId: 1, sequence: 5, value: "tree-1-seq-5" },
    { treeInstanceId: 1, sequence: 2, value: "tree-1-seq-2" }
  ];
  equal(reduceOne("first", "FixedString32", keyed).value, "tree-1-seq-2", "First uses explicit stable key");
  equal(reduceOne("last", "FixedString32", keyed).value, "tree-2", "Last uses explicit stable key");
  check(!reduceOne("sum", "Int32", [
    { treeInstanceId: 1, sequence: 0, value: 2147483647 },
    { treeInstanceId: 2, sequence: 0, value: 1 }
  ]).ok, "Int32 sum overflow is rejected");
  check(!reduceOne("sum", "Float32", [
    { treeInstanceId: 1, sequence: 0, value: 3.4028234663852886e38 },
    { treeInstanceId: 2, sequence: 0, value: 3.4028234663852886e38 }
  ]).ok, "non-finite float intermediate is rejected");
  check(!reduceOne("min", "Float64", [{ treeInstanceId: 1, sequence: 0, value: Number.NaN }]).ok, "NaN is rejected");
  equal(reduceOne("sum", "Float32", [{ treeInstanceId: 1, sequence: 0, value: -0 }]).value, 0, "negative zero canonicalizes to positive zero");
  check(!Object.is(reduceOne("sum", "Float32", [{ treeInstanceId: 1, sequence: 0, value: -0 }]).value, -0), "zero result is physically positive");
  check(!reduceOne("sum", "Int32", [
    { treeInstanceId: 1, sequence: 0, value: 1 },
    { treeInstanceId: 1, sequence: 0, value: 2 }
  ]).ok, "duplicate stable contribution keys are rejected");
  equal(reduceOne("sum", "Int64", [{ treeInstanceId: 1, sequence: 0, value: 9223372036854775807n }]).value,
    9223372036854775807n, "Int64 maximum boundary is exact");
  equal(reduceOne("sum", "Int64", [{ treeInstanceId: 1, sequence: 0, value: -9223372036854775808n }]).value,
    -9223372036854775808n, "Int64 minimum boundary is exact");
  check(!reduceOne("sum", "Int64", [
    { treeInstanceId: 1, sequence: 0, value: 9223372036854775807n },
    { treeInstanceId: 2, sequence: 0, value: 1n }
  ]).ok, "Int64 positive overflow is rejected exactly");
  check(!reduceOne("sum", "Int64", [
    { treeInstanceId: 1, sequence: 0, value: -9223372036854775808n },
    { treeInstanceId: 2, sequence: 0, value: -1n }
  ]).ok, "Int64 negative overflow is rejected exactly");
  check(!reduceOne("first", "example.Target", [
    { treeInstanceId: 1, sequence: 0, value: { entity: 1 } },
    { treeInstanceId: 2, sequence: 0, value: { entity: 2, weight: 0 } }
  ]).ok, "First validates every registered input before selection");
  check(!reduceOne("last", "example.Target", [
    { treeInstanceId: 1, sequence: 0, value: { entity: 1, weight: 0 } },
    { treeInstanceId: 2, sequence: 0, value: { entity: 2 } }
  ]).ok, "Last validates every registered input before selection");
  const enumContributions = [
    { treeInstanceId: 2, sequence: 0, value: { contract: "aibt.enum.mode", value: 2 } },
    { treeInstanceId: 1, sequence: 0, value: { contract: "aibt.enum.mode", value: 1 } }
  ];
  equal(reduceOne("first", "Enum32", enumContributions, { typeVersion: 1, enumContract: "aibt.enum.mode" }).value,
    { contract: "aibt.enum.mode", value: 1 }, "Enum32 First preserves and validates its declared contract");
  equal(reduceOne("last", "Enum32", enumContributions, { typeVersion: 1, enumContract: "aibt.enum.mode" }).value,
    { contract: "aibt.enum.mode", value: 2 }, "Enum32 Last preserves and validates its declared contract");
  const mismatchedEnumContributions = clone(enumContributions);
  mismatchedEnumContributions[1].value.contract = "aibt.enum.other";
  check(!reduceOne("first", "Enum32", mismatchedEnumContributions, { typeVersion: 1, enumContract: "aibt.enum.mode" }).ok,
    "Enum32 First rejects a mismatched contribution contract");
  check(!reduceOne("last", "Enum32", mismatchedEnumContributions, { typeVersion: 1, enumContract: "aibt.enum.mode" }).ok,
    "Enum32 Last rejects a mismatched contribution contract");

  const intSlot = { slotIndex: 1, kind: "sum", typeId: "Int32", typeVersion: 1 };
  const boolSlot = { slotIndex: 0, kind: "any", typeId: "Bool", typeVersion: 1 };
  const bounded = createContributionStream(7, 2);
  check(appendContribution(bounded, intSlot, 10), "first bounded contribution is accepted");
  check(appendContribution(bounded, intSlot, 20), "second bounded contribution is accepted");
  check(!appendContribution(bounded, intSlot, 30), "per-instance capacity exhaustion is rejected");
  check(!bounded.valid, "capacity exhaustion invalidates the participating stream");
  equal(bounded.entries, [], "invalid bounded stream publishes no partial contribution set");
  const invalidIdentityStreams = [];
  for (const invalidTreeId of [0, -1n, maxU64 + 1n]) {
    const invalidIdentityStream = createContributionStream(invalidTreeId, 1);
    check(!invalidIdentityStream.valid, `Shared stream rejects invalid TreeInstanceId ${invalidTreeId}`);
    invalidIdentityStreams.push([invalidTreeId, invalidIdentityStream]);
  }

  const originalShared = {
    values: { 0: false, 1: 10 },
    versions: { 0: "4", 1: "8" },
    revision: "12"
  };
  for (const [invalidTreeId, invalidIdentityStream] of invalidIdentityStreams) {
    const invalidIdentityReduce = reduceContext(originalShared, [boolSlot, intSlot], [invalidIdentityStream]);
    check(!invalidIdentityReduce.ok && invalidIdentityReduce.error === "invalid-stream",
      `Shared stream TreeInstanceId ${invalidTreeId} is rejected before reduction`);
    equal(invalidIdentityReduce.state, originalShared,
      `Shared stream TreeInstanceId ${invalidTreeId} rejects the whole context without mutation`);
  }
  for (const kind of ["first", "last"]) {
    const enumSlot = { slotIndex: 2, kind, typeId: "Enum32", typeVersion: 1, enumContract: "aibt.enum.mode" };
    const enumStream = createContributionStream(kind === "first" ? 31 : 32, 2);
    check(appendContribution(enumStream, boolSlot, true), `Enum32 ${kind} atomic canary stages an earlier Bool`);
    check(appendContribution(enumStream, enumSlot, { contract: "aibt.enum.mode", value: 7 }),
      `Enum32 ${kind} atomic canary accepts a matching value before corruption`);
    enumStream.entries[1].value.contract = "aibt.enum.other";
    const enumState = {
      values: { 0: false, 2: { contract: "aibt.enum.mode", value: 3 } },
      versions: { 0: "4", 2: "6" },
      revision: "9"
    };
    const rejectedEnum = reduceContext(enumState, [boolSlot, enumSlot], [enumStream]);
    check(!rejectedEnum.ok, `Enum32 ${kind} mismatched contract rejects whole-context Reduce`);
    equal(rejectedEnum.state, enumState, `Enum32 ${kind} mismatch publishes no earlier slot/version/revision mutation`);
  }
  const streamOne = createContributionStream(1, 2);
  check(appendContribution(streamOne, boolSlot, true), "typed Bool contribution is accepted");
  check(appendContribution(streamOne, intSlot, 2147483647), "typed Int32 contribution is accepted");
  const streamTwo = createContributionStream(2, 1);
  check(appendContribution(streamTwo, intSlot, 1), "second typed Int32 contribution is accepted");
  const atomic = reduceContext(originalShared, [boolSlot, intSlot], [streamOne, streamTwo]);
  check(!atomic.ok, "one invalid slot rejects Reduce");
  equal(atomic.state, originalShared, "whole-context Reduce failure is atomic");
  const unchanged = reduceContext(originalShared, [boolSlot], [createContributionStream(1, 1)]);
  check(unchanged.ok && !unchanged.changed, "no contributions produce no write");
  equal(unchanged.state, originalShared, "no-contribution state is unchanged");
  const invalidStreamReduce = reduceContext(originalShared, [intSlot], [bounded]);
  check(!invalidStreamReduce.ok, "invalid participating stream rejects the entire Shared update");
  equal(invalidStreamReduce.state, originalShared, "invalid stream Reduce publishes no mutation");
  const metadataMismatch = createContributionStream(4, 1);
  check(appendContribution(metadataMismatch, intSlot, 1), "metadata fixture contribution is accepted");
  metadataMismatch.entries[0].typeVersion = 2;
  check(!reduceContext(originalShared, [intSlot], [metadataMismatch]).ok, "contribution type/version metadata mismatch is rejected");
  const capacityMismatch = createContributionStream(6, 1);
  check(appendContribution(capacityMismatch, intSlot, 1), "capacity metadata fixture contribution is accepted");
  capacityMismatch.entries[0].capacity = 2;
  check(!reduceContext(originalShared, [intSlot], [capacityMismatch]).ok, "contribution capacity metadata mismatch is rejected");
  const duplicateA = createContributionStream(5, 1);
  const duplicateB = createContributionStream(5, 1);
  check(appendContribution(duplicateA, boolSlot, true), "first global-key fixture is accepted");
  check(appendContribution(duplicateB, intSlot, 1), "second global-key fixture is accepted before merge");
  check(!reduceContext(originalShared, [boolSlot, intSlot], [duplicateA, duplicateB]).ok,
    "semantic contribution keys are globally unique across slots and streams");
  const registeredSlot = { slotIndex: 2, kind: "last", typeId: "example.Target", typeVersion: 2 };
  const equalityStream = createContributionStream(8, 2);
  check(appendContribution(equalityStream, boolSlot, true), "pre-equality Bool contribution is accepted");
  check(appendContribution(equalityStream, registeredSlot, { entity: 2, weight: 0 }),
    "registered equality fixture contribution is accepted");
  const equalityState = {
    values: { 0: false, 2: { entity: 1, weight: 0 } },
    versions: { 0: "2", 2: "3" },
    revision: "4"
  };
  const sharedEqualityFailure = reduceContext(equalityState, [boolSlot, registeredSlot], [equalityStream], {
    2: () => { throw new Error("registered equality fixture failure"); }
  });
  check(!sharedEqualityFailure.ok && sharedEqualityFailure.error === "equality-failure",
    "registered Shared equality failure rejects Reduce");
  equal(sharedEqualityFailure.state, equalityState,
    "registered Shared equality failure publishes no earlier staged mutation, version, or revision");
  const missingSharedEquality = reduceContext(equalityState, [boolSlot, registeredSlot], [equalityStream]);
  check(!missingSharedEquality.ok && missingSharedEquality.error === "equality-failure",
    "registered Shared equality callback is required");
  equal(missingSharedEquality.state, equalityState,
    "missing registered Shared equality publishes no staged mutation");
  const invalidSharedEquality = reduceContext(equalityState, [boolSlot, registeredSlot], [equalityStream], {
    2: () => "not-a-boolean"
  });
  check(!invalidSharedEquality.ok && invalidSharedEquality.error === "equality-failure",
    "registered Shared equality must return a Boolean");
  equal(invalidSharedEquality.state, equalityState,
    "invalid registered Shared equality result publishes no staged mutation");
  const successfulSharedEquality = reduceContext(equalityState, [boolSlot, registeredSlot], [equalityStream], {
    2: () => false
  });
  check(successfulSharedEquality.ok && successfulSharedEquality.changed,
    "registered Shared equality participates in a successful atomic commit");
  equal(successfulSharedEquality.state, {
    values: { 0: true, 2: { entity: 2, weight: 0 } },
    versions: { 0: "3", 2: "4" },
    revision: "5"
  }, "successful registered Shared equality publishes all staged slots once");

  const initialAgent = createAgentState(valid);
  equal(initialAgent.versions, { agent_health: "0", agent_ready: "0" }, "Agent slot versions initialize to zero");
  equal(initialAgent.revision, "0", "Agent revision initializes to zero");
  const dirtyAgent = clone(initialAgent);
  dirtyAgent.values.agent_health = 1;
  dirtyAgent.versions.agent_health = "4";
  const reset = resetAgentState(dirtyAgent, valid);
  check(reset.ok && reset.changed, "Agent reset commits changed defaults");
  equal(reset.state.values.agent_health, 100, "Agent reset restores default");
  equal(reset.state.versions.agent_health, "5", "changed Agent slot increments once");
  equal(reset.state.versions.agent_ready, "0", "equal Agent slot remains unchanged");
  equal(reset.state.revision, "1", "Agent context revision increments once");
  const noOpReset = resetAgentState(reset.state, valid);
  check(noOpReset.ok && !noOpReset.changed, "equal Agent reset is a no-op");
  const overflowAgent = clone(dirtyAgent);
  overflowAgent.versions.agent_health = maxU64.toString();
  const rejectedReset = resetAgentState(overflowAgent, valid);
  check(!rejectedReset.ok, "Agent reset version overflow is rejected");
  equal(rejectedReset.state, overflowAgent, "Agent reset overflow publishes nothing");

  const descriptor = agentDescriptor(valid, layout);
  const registry = new AgentContextRegistryModel();
  equal(registry.create(0, descriptor, initialAgent), null, "zero AgentId is rejected");
  equal(registry.create(-1n, descriptor, initialAgent), null, "negative AgentId is rejected");
  equal(registry.create(maxU64 + 1n, descriptor, initialAgent), null, "overflowing AgentId is rejected");
  const context = registry.create(77, descriptor, initialAgent);
  check(context !== null, "nonzero unique AgentId creates a context");
  equal(registry.create(77, descriptor, initialAgent), null, "duplicate live AgentId is rejected");
  const layoutDescriptorMismatch = agentDescriptor(valid, mismatchLayout);
  check(!context.bind(1, layoutDescriptorMismatch), "Agent binding rejects layout mismatch");
  const accessDescriptorMismatch = clone(descriptor);
  accessDescriptorMismatch.access[0].slotIndex = 1;
  check(!context.bind(1, accessDescriptorMismatch), "Agent binding rejects access mismatch");
  check(!context.bind(0, descriptor), "zero TreeInstanceId binding is rejected");
  check(!context.bind(-1n, descriptor), "negative TreeInstanceId binding is rejected");
  check(!context.bind(maxU64 + 1n, descriptor), "overflowing TreeInstanceId binding is rejected");
  check(context.bind(9, descriptor), "first compatible tree binds");
  check(context.bind(3, descriptor), "second compatible tree binds");
  const alternateAccess = clone(descriptor);
  alternateAccess.access = [{ ordinal: 7, slotIndex: 1, mode: "read", typeId: "Bool", typeVersion: 1 }];
  check(context.bind(5, alternateAccess), "different compatible Agent access subset binds to the same exact context layout");
  check(!context.rebind(3, descriptor), "active Agent binding is immutable");
  check(!context.beginEligiblePass([0]), "zero TreeInstanceId pass input is rejected");
  check(!context.beginEligiblePass([-1n]), "negative TreeInstanceId pass input is rejected");
  check(!context.beginEligiblePass([maxU64 + 1n]), "overflowing TreeInstanceId pass input is rejected");
  check(context.beginEligiblePass([9, 3, 5]), "eligible Agent pass begins");
  check(!context.bind(11, descriptor), "Agent binding mutation is rejected while ordered ownership is active");
  let callbacks = 0;
  check(!context.tryExecute(maxU64 + 1n, () => callbacks++), "overflowing lease owner is rejected");
  equal(callbacks, 0, "invalid lease owner rejects before callback");
  check(!context.tryExecute(9, () => callbacks++), "out-of-order Agent lease is rejected");
  equal(callbacks, 0, "out-of-order lease rejects before callback");
  check(context.tryExecute(3, () => callbacks++), "smallest TreeInstanceId acquires first lease");
  check(!context.tryExecute(5, () => callbacks++), "concurrent Agent lease is rejected");
  equal(callbacks, 1, "concurrent lease rejects before callback");
  check(!context.reset(valid).ok, "Agent reset is rejected while an Execute lease is live");
  check(!context.unbind(3), "Agent unbind is rejected while an Execute lease is live");
  check(context.release(3), "first Agent lease releases");
  check(!context.reset(valid).ok, "Agent reset remains rejected while ordered pass ownership is active");
  check(context.tryExecute(5, () => callbacks++), "second TreeInstanceId acquires next lease");
  check(context.release(5), "second Agent lease releases");
  check(context.tryExecute(9, () => callbacks++), "last TreeInstanceId acquires final lease");
  check(context.release(9), "final Agent lease releases");
  equal(callbacks, 3, "only ordered owners execute callbacks");
  context.state = clone(dirtyAgent);
  const beforeEqualityFailure = clone(context.state);
  const equalityFailure = context.reset(valid, { agent_health: () => { throw new Error("fixture equality failure"); } });
  check(!equalityFailure.ok && equalityFailure.error === "equality-failure", "Agent reset propagates deterministic equality failure");
  equal(context.state, beforeEqualityFailure, "Agent equality failure is atomic");
  check(!context.dispose(), "bound Agent context cannot dispose");
  for (const treeId of [9, 3, 5]) check(context.unbind(treeId), `quiescent tree ${treeId} unbinds`);
  check(context.dispose(), "unbound quiescent Agent context disposes");
  check(registry.destroy(77), "disposed Agent context unregisters deterministically");
  check(registry.create(77, descriptor, initialAgent) !== null, "AgentId can be reused only after disposal and unregister");

  process.stdout.write(`Blackboard scope model: PASS (${assertions} assertions)\n`);
  process.stdout.write(`agent schema ${hashes.agentSchema}\n`);
  process.stdout.write(`shared schema ${hashes.sharedSchema}\n`);
  process.stdout.write(`compiled content ${hashes.compiledContent}\n`);
}

run();
