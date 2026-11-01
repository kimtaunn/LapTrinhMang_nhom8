"use strict";

const boardElement = document.getElementById("board");
const statusElement = document.getElementById("status");
const connectionElement = document.getElementById("connection");
const roomCodeInput = document.getElementById("room-code");
const roomCodeDisplay = document.getElementById("room-code-display");
const copyButton = document.getElementById("copy-room");
const copyLabel = document.getElementById("copy-label");
const restartButton = document.getElementById("restart");
const forfeitButton = document.getElementById("forfeit");
const chatInput = document.getElementById("chat-input");
const chatForm = document.getElementById("chat-form");
const messages = document.getElementById("messages");
const emptyChat = document.getElementById("empty-chat");
const soundToggle = document.getElementById("sound-toggle");
let mySide = null;
let currentState = null;
let restartPromptOpen = false;
let timerInterval = null;

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/game")
  .withAutomaticReconnect()
  .build();

try {
  soundToggle.checked = localStorage.getItem("caro-sound") !== "off";
} catch {
  soundToggle.checked = true;
}

function playMoveSound() {
  if (!soundToggle.checked) return;
  try {
    const audio = new AudioContext();
    const oscillator = audio.createOscillator();
    const gain = audio.createGain();
    oscillator.type = "sine";
    oscillator.frequency.value = 620;
    gain.gain.setValueAtTime(0.08, audio.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + 0.09);
    oscillator.connect(gain);
    gain.connect(audio.destination);
    oscillator.start();
    oscillator.stop(audio.currentTime + 0.09);
    oscillator.onended = () => audio.close();
  } catch {
    // Audio is optional; the game remains usable when the browser blocks it.
  }
}

function appendMessage(text, kind = "", side = null) {
  emptyChat.hidden = true;
  const item = document.createElement("p");
  item.className = `message ${kind}`.trim();
  if (kind !== "system" && side && side !== mySide)
    item.classList.add("opponent");
  item.textContent = text;

  if (kind !== "system") {
    const time = document.createElement("span");
    time.className = "message-time";
    time.textContent = new Intl.DateTimeFormat("vi-VN", {
      hour: "2-digit",
      minute: "2-digit"
    }).format(new Date());
    item.appendChild(time);
  }

  messages.appendChild(item);
  messages.scrollTop = messages.scrollHeight;
}

function setConnected(connected) {
  connectionElement.textContent = connected ? "Đã kết nối" : "Mất kết nối";
  connectionElement.className = `connection ${connected ? "online" : "offline"}`;
  document.getElementById("connection-note").textContent =
    connected ? "Kết nối ổn định" : "Đang thử kết nối lại";
  document.getElementById("create-room").disabled = !connected || mySide !== null;
  document.getElementById("join-room").disabled = !connected || mySide !== null;
}

function updateTurnClock() {
  if (!currentState || !currentState.turnStartedAt) {
    document.getElementById("clock-x").textContent = "00:00";
    document.getElementById("clock-o").textContent = "00:00";
    return;
  }

  if (!currentState.hasOpponent) {
    document.getElementById("clock-x").textContent = "00:00";
    document.getElementById("clock-o").textContent = "00:00";
    return;
  }

  const startedAt = new Date(currentState.turnStartedAt).getTime();
  const endedAt = currentState.turnFinishedAt ? new Date(currentState.turnFinishedAt).getTime() : Date.now();
  const elapsed = Math.max(0, Math.floor((endedAt - startedAt) / 1000));
  const formatted = `${String(Math.floor(elapsed / 60)).padStart(2, "0")}:${String(elapsed % 60).padStart(2, "0")}`;
  document.getElementById("clock-x").textContent = currentState.nextTurn === "X" ? formatted : "00:00";
  document.getElementById("clock-o").textContent = currentState.nextTurn === "O" ? formatted : "00:00";
}

function renderBoard(state) {
  currentState = state;
  boardElement.replaceChildren();
  const canPlay = Boolean(mySide && state.hasOpponent && !state.winner && state.nextTurn === mySide);
  const liveBadge = document.querySelector(".live-badge");
  liveBadge.innerHTML = "";
  const badgeDot = document.createElement("span");
  liveBadge.append(badgeDot, document.createTextNode(
    !mySide ? " SẴN SÀNG" : !state.hasOpponent ? " ĐANG CHỜ" : state.winner ? " ĐÃ KẾT THÚC" : " ĐANG DIỄN RA"
  ));

  for (let row = 0; row < 15; row++) {
    for (let column = 0; column < 15; column++) {
      const mark = state.board[row][column];
      const cell = document.createElement("button");
      cell.type = "button";
      cell.className = `cell ${mark === "." ? "" : mark.toLowerCase()}`.trim();
      if (state.lastRow === row && state.lastColumn === column)
        cell.classList.add("last-move");
      cell.textContent = mark === "." ? "" : mark;
      cell.setAttribute("aria-label", `Hàng ${row + 1}, cột ${column + 1}${mark === "." ? "" : `, quân ${mark}`}`);
      cell.disabled = !canPlay || mark !== ".";
      cell.addEventListener("click", async () => {
        try {
          await connection.invoke("MakeMove", row, column);
        } catch (error) {
          appendMessage(`Không gửi được nước đi: ${error.message}`, "system");
        }
      });
      boardElement.appendChild(cell);
    }
  }

  document.getElementById("name-x").textContent = mySide === "X" ? "Bạn" : state.hasOpponent ? "Đối thủ" : "Người chơi X";
  document.getElementById("name-o").textContent = mySide === "O" ? "Bạn" : state.hasOpponent ? "Đối thủ" : "Đang chờ";
  document.getElementById("avatar-x").textContent = "X";
  document.getElementById("avatar-o").textContent = state.hasOpponent ? "O" : "…";
  document.getElementById("score-x").textContent = String(state.scoreX ?? 0);
  document.getElementById("score-o").textContent = String(state.scoreO ?? 0);
  document.getElementById("round-label").textContent = `VÁN ${Math.min(state.round ?? 1, 3)}/3`;
  document.getElementById("online-count").textContent = state.hasOpponent ? "2" : "1";
  document.getElementById("player-card-x").classList.toggle("active-turn", state.hasOpponent && !state.winner && state.nextTurn === "X");
  document.getElementById("player-card-o").classList.toggle("active-turn", state.hasOpponent && !state.winner && state.nextTurn === "O");

  if (!state.hasOpponent) {
    statusElement.textContent = mySide ? "Đang chờ người chơi thứ hai..." : "Tạo phòng để bắt đầu";
    document.getElementById("turn-hint").textContent = "Gửi mã phòng cho bạn bè để bắt đầu";
  } else if (state.winner === "draw") {
    statusElement.textContent = "Ván đấu hòa";
    document.getElementById("turn-hint").textContent = "Chơi lại để bắt đầu ván tiếp theo";
  } else if (state.winner) {
    statusElement.textContent = state.winner === mySide ? "Bạn đã chiến thắng!" : `Quân ${state.winner} chiến thắng`;
    document.getElementById("turn-hint").textContent = "Ván đấu đã kết thúc";
  } else {
    statusElement.textContent = state.nextTurn === mySide ? "Lượt của bạn" : `Đang chờ quân ${state.nextTurn}`;
    document.getElementById("turn-hint").textContent = state.nextTurn === mySide ? "Đến lượt bạn chọn nước đi" : "Đối thủ đang suy nghĩ";
  }

  document.getElementById("turn-icon").textContent = state.hasOpponent ? state.nextTurn : "X";
  document.getElementById("turn-label-x").textContent = state.hasOpponent && !state.winner && state.nextTurn === "X" ? "ĐANG ĐI" : "QUÂN X";
  document.getElementById("turn-label-o").textContent = state.hasOpponent && !state.winner && state.nextTurn === "O" ? "ĐANG ĐI" : state.hasOpponent ? "QUÂN O" : "ĐANG CHỜ";
  restartButton.disabled = !state.hasOpponent;
  forfeitButton.disabled = !canPlay && (!state.hasOpponent || Boolean(state.winner));
  chatInput.disabled = !state.hasOpponent;
  document.getElementById("send-chat").disabled = !state.hasOpponent;
  updateTurnClock();
}

function clearLocalRoom(status, details) {
  mySide = null;
  currentState = null;
  roomCodeDisplay.textContent = "CHƯA TẠO";
  copyButton.disabled = true;
  renderBoard({
    board: Array.from({ length: 15 }, () => ".".repeat(15)),
    nextTurn: "X",
    winner: null,
    hasOpponent: false,
    round: 1
  });
  statusElement.textContent = status;
  document.getElementById("turn-hint").textContent = details;
  restartButton.disabled = true;
  forfeitButton.disabled = true;
  chatInput.disabled = true;
  document.getElementById("send-chat").disabled = true;
  setConnected(connection.state === signalR.HubConnectionState.Connected);
}

document.getElementById("create-room").addEventListener("click", async () => {
  try {
    await connection.invoke("CreateRoom");
  } catch (error) {
    appendMessage(`Không tạo được phòng: ${error.message}`, "system");
  }
});

document.getElementById("join-room").addEventListener("click", async () => {
  try {
    await connection.invoke("JoinRoom", roomCodeInput.value);
  } catch (error) {
    appendMessage(`Không vào được phòng: ${error.message}`, "system");
  }
});

roomCodeInput.addEventListener("input", () => {
  roomCodeInput.value = roomCodeInput.value.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6);
});

copyButton.addEventListener("click", async () => {
  const code = roomCodeDisplay.textContent;
  if (!code || code === "CHƯA TẠO") return;
  try {
    await navigator.clipboard.writeText(code);
  } catch {
    const temporaryInput = document.createElement("textarea");
    temporaryInput.value = code;
    document.body.appendChild(temporaryInput);
    temporaryInput.select();
    document.execCommand("copy");
    temporaryInput.remove();
  }
  copyLabel.textContent = "Đã chép";
  window.setTimeout(() => { copyLabel.textContent = "Sao chép"; }, 1600);
});

restartButton.addEventListener("click", async () => {
  try {
    await connection.invoke("RequestRestart");
  } catch (error) {
    appendMessage(`Không gửi được yêu cầu: ${error.message}`, "system");
  }
});

forfeitButton.addEventListener("click", async () => {
  if (!window.confirm("Bạn chắc chắn muốn xin thua ván này?")) return;
  try {
    await connection.invoke("Forfeit");
  } catch (error) {
    appendMessage(`Không gửi được yêu cầu xin thua: ${error.message}`, "system");
  }
});

chatForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const text = chatInput.value.trim();
  if (!text) return;
  try {
    await connection.invoke("SendChat", text);
    chatInput.value = "";
  } catch (error) {
    appendMessage(`Không gửi được tin nhắn: ${error.message}`, "system");
  }
});

soundToggle.addEventListener("change", () => {
  try {
    localStorage.setItem("caro-sound", soundToggle.checked ? "on" : "off");
  } catch {
    appendMessage("Không lưu được tùy chọn âm thanh trên trình duyệt này.", "system");
  }
});

connection.on("RoomCreated", (code, side) => {
  mySide = side;
  roomCodeInput.value = code;
  roomCodeDisplay.textContent = code;
  copyButton.disabled = false;
  appendMessage(`Đã tạo phòng ${code}. Gửi mã này cho bạn bè để cùng chơi.`, "system");
  setConnected(connection.state === signalR.HubConnectionState.Connected);
});

connection.on("JoinedRoom", (code, side) => {
  mySide = side;
  roomCodeInput.value = code;
  roomCodeDisplay.textContent = code;
  copyButton.disabled = false;
  appendMessage(`Bạn đã vào phòng ${code}.`, "system");
  setConnected(connection.state === signalR.HubConnectionState.Connected);
});

connection.on("GameState", (state) => {
  const previousNextTurn = currentState?.nextTurn;
  const hadOpponent = currentState?.hasOpponent;
  renderBoard(state);
  if (hadOpponent && previousNextTurn !== state.nextTurn && state.hasOpponent && !state.winner)
    playMoveSound();
});
connection.on("ErrorMessage", (text) => appendMessage(text, "system"));
connection.on("SystemMessage", (text) => appendMessage(text, "system"));
connection.on("ChatMessage", (side, text) => appendMessage(text, "", side));
connection.on("OpponentDisconnected", () => {
  appendMessage("Đối thủ đã rời phòng. Bạn có thể chờ người chơi khác vào.", "system");
  if (mySide === "O") {
    connection.invoke("LeaveRoom").catch((error) => appendMessage(`Lỗi rời phòng: ${error.message}`, "system"));
    clearLocalRoom("Đối thủ đã rời phòng", "Nhập lại mã phòng nếu muốn chơi tiếp.");
  } else if (currentState) {
    renderBoard({ ...currentState, hasOpponent: false });
  }
});
connection.on("RoomClosed", () => {
  appendMessage("Người tạo phòng đã rời đi; phòng đã đóng.", "system");
  connection.invoke("LeaveRoom").catch((error) => appendMessage(`Lỗi rời phòng: ${error.message}`, "system"));
  roomCodeInput.value = "";
  clearLocalRoom("Phòng đã đóng", "Tạo phòng mới hoặc nhập mã của bạn bè.");
});
connection.on("RestartRequested", async (side) => {
  if (restartPromptOpen) return;
  restartPromptOpen = true;
  const accepted = window.confirm(`Người chơi quân ${side} muốn chơi lại. Bạn đồng ý không?`);
  restartPromptOpen = false;
  try {
    await connection.invoke("RespondRestart", accepted);
  } catch (error) {
    appendMessage(`Không phản hồi được: ${error.message}`, "system");
  }
});
connection.on("RestartDeclined", () => appendMessage("Đối thủ từ chối chơi lại.", "system"));
connection.onreconnecting(() => {
  connectionElement.textContent = "Đang kết nối lại...";
  connectionElement.className = "connection";
  clearLocalRoom("Đang kết nối lại...", "Mất kết nối sẽ đóng phòng hiện tại.");
});
connection.onreconnected(() => {
  setConnected(true);
  appendMessage("Đã kết nối lại. Hãy tạo phòng mới hoặc nhập lại mã phòng.", "system");
});
connection.onclose(() => {
  clearLocalRoom("Mất kết nối máy chủ", "Hãy tải lại trang hoặc kiểm tra mạng.");
  setConnected(false);
});

async function start() {
  try {
    await connection.start();
    setConnected(true);
  } catch (error) {
    setConnected(false);
    appendMessage(`Không kết nối được máy chủ: ${error.message}`, "system");
    window.setTimeout(start, 3000);
  }
}

renderBoard({
  board: Array.from({ length: 15 }, () => ".".repeat(15)),
  nextTurn: "X",
  winner: null,
  hasOpponent: false,
  round: 1,
  scoreX: 0,
  scoreO: 0
});
timerInterval = window.setInterval(updateTurnClock, 1000);
start();
