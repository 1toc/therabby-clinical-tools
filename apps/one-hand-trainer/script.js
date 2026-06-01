let hits = 0;
let times = [];
let startTime = 0;
let active = false;
const target = document.getElementById("target");

function showTarget() {
  active = true;
  startTime = performance.now();
  target.textContent = "Click now";
  target.style.transform = `translate(${Math.floor(Math.random()*40)-20}px, ${Math.floor(Math.random()*40)-20}px)`;
}

function update() {
  document.getElementById("hits").textContent = hits;
  if (times.length) {
    const avg = Math.round(times.reduce((a,b) => a + b, 0) / times.length);
    document.getElementById("avg").textContent = avg;
  }
}

document.getElementById("start").addEventListener("click", () => {
  hits = 0;
  times = [];
  update();
  target.textContent = "Wait...";
  setTimeout(showTarget, 800);
});

target.addEventListener("click", () => {
  if (!active) return;
  const reaction = performance.now() - startTime;
  times.push(reaction);
  hits++;
  active = false;
  update();
  target.textContent = "Wait...";
  if (hits < 10) {
    setTimeout(showTarget, 600 + Math.random() * 700);
  } else {
    target.textContent = "Finished";
    target.style.transform = "none";
  }
});
