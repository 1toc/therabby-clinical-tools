const fields = ["activity", "context", "factors", "skills", "environment", "meaning", "adaptation"];
const get = (id) => document.getElementById(id).value.trim() || "Not specified";

document.getElementById("generate").addEventListener("click", () => {
  const note = `# Activity Analysis Note

Activity: ${get("activity")}

## Target user/context
${get("context")}

## Required body functions and client factors
${get("factors")}

## Performance skills and process demands
${get("skills")}

## Environment and tools
${get("environment")}

## Meaning, motivation, and participation value
${get("meaning")}

## Adaptation ideas
${get("adaptation")}

## Clinical reminder
This note is for education and clinical reasoning support. It does not replace professional assessment or clinical judgment.`;
  document.getElementById("result").textContent = note;
});
