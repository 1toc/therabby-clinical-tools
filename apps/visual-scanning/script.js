const symbols = ["●", "▲", "■", "◆", "★", "✚", "○", "△", "□", "◇"];
let target = "★";
let score = 0;
let attempts = 0;

function pick(arr) { return arr[Math.floor(Math.random() * arr.length)]; }

function newRound() {
  target = pick(symbols);
  document.getElementById("target-symbol").textContent = target;
  const grid = document.getElementById("grid");
  grid.innerHTML = "";
  const targetIndex = Math.floor(Math.random() * 25);
  for (let i = 0; i < 25; i++) {
    const button = document.createElement("button");
    button.className = "target";
    button.type = "button";
    button.textContent = i === targetIndex ? target : pick(symbols.filter(s => s !== target));
    button.setAttribute("aria-label", `cell ${i + 1}: ${button.textContent}`);
    button.addEventListener("click", () => {
      attempts++;
      if (button.textContent === target) {
        score++;
        button.classList.add("correct");
        setTimeout(newRound, 600);
      }
      updateStats();
    });
    grid.appendChild(button);
  }
}
function updateStats() {
  document.getElementById("score").textContent = score;
  document.getElementById("attempts").textContent = attempts;
}

document.getElementById("new-round").addEventListener("click", newRound);
newRound();
