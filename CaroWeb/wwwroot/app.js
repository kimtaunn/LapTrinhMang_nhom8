"use strict";

const boardElement = document.getElementById("board");
const statusElement = document.getElementById("status");
const connectionElement = document.getElementById("connection");
const roomCodeInput = document.getElementById("room-code");
const roomCodeDisplay = document.getElementById("room-code-display");
const copyButton = document.getElementById("copy-room");
const copyLabel = document.getElementById("copy-label");
const restartButton = document.getElementById("restart");
const undoButton = document.getElementById("undo");
const forfeitButton = document.getElementById("forfeit");
const leaveRoomButton = document.getElementById("leave-room");
const createBotButton = document.getElementById("create-bot");
const botDifficulty = document.getElementById("bot-difficulty");
const chatInput = document.getElementById("chat-input");
const chatForm = document.getElementById("chat-form");
const messages = document.getElementById("messages");
const emptyChat = document.getElementById("empty-chat");
const soundToggle = document.getElementById("sound-toggle");
const nicknameScreen = document.getElementById("nickname-screen");
const gameScreen = document.getElementById("game-screen");
const nicknameInput = document.getElementById("nickname-input");
const nicknameError = document.getElementById("nickname-error");
const nicknameConnection = document.getElementById("nickname-connection");
const nicknameCreateButton = document.getElementById("nickname-create");
const nicknameJoinButton = document.getElementById("nickname-join");
const nicknameJoinCode = document.getElementById("nickname-room-code");
const nicknameBotButton = document.getElementById("nickname-bot");
const nicknameBotDifficulty = document.getElementById("nickname-bot-difficulty");
const gamePage = document.getElementById("game");
const historyPage = document.getElementById("history-page");
const historyList = document.getElementById("history-list");
const historyStorageKey = "caro-game-history";
const playerTokenStorageKey = "caro-player-token";
const nicknameStorageKey = "caro-player-nickname";
const maxHistoryEntries = 5;
let mySide = null;
let isBotGame = false;
let botDifficultyLabel = "";
let activeRoomCode = null;
let currentState = null;
let pendingMove = null;
let playerToken = null;
let playerNickname = "";
let reconnectTimer = null;
let rejoinInProgress = false;
let activeRequestPrompt = null;
let pendingRequest = null;
let shownGameOverKey = null;
let timerInterval = null;
let gameHistory = [];
const recordedGameIds = new Set();

try {
  playerToken = sessionStorage.getItem(playerTokenStorageKey);
} catch {
  playerToken = null;
}

try {
  playerNickname = sessionStorage.getItem(nicknameStorageKey) || "";
} catch {
  playerNickname = "";
}

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/game")
  .withAutomaticReconnect([0, 1000, 3000, 5000, 10000, 15000])
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

function loadGameHistory() {
  try {
    const saved = JSON.parse(localStorage.getItem(historyStorageKey) || "[]");
    if (!Array.isArray(saved)) throw new Error("Dữ liệu lịch sử không đúng định dạng.");
    return saved.filter((entry) =>
      entry && typeof entry.id === "string" &&
      typeof entry.playedAt === "string" &&
      typeof entry.result === "string" &&
      typeof entry.opponent === "string"
    ).slice(0, maxHistoryEntries);
  } catch (error) {
    appendMessage(`Không đọc được lịch sử ván đấu: ${error.message}`, "system");
    return [];
  }
}

function saveGameHistory() {
  try {
    localStorage.setItem(historyStorageKey, JSON.stringify(gameHistory));
  } catch (error) {
    appendMessage(`Không lưu được lịch sử ván đấu: ${error.message}`, "system");
  }
}

function renderGameHistory() {
  historyList.replaceChildren();
  if (gameHistory.length === 0) {
    const empty = document.createElement("p");
    empty.className = "history-empty";
    empty.textContent = "Chưa có ván đấu nào được lưu trên trình duyệt này.";
    historyList.appendChild(empty);
    return;
  }

  for (const entry of gameHistory) {
    const card = document.createElement("article");
    card.className = "history-entry";
    const result = document.createElement("strong");
    result.className = `history-result ${entry.result}`;
    result.textContent = entry.result === "win" ? "Chiến thắng" :
      entry.result === "loss" ? "Thất bại" : "Hòa";
    const details = document.createElement("div");
    details.className = "history-details";
    const opponent = document.createElement("span");
    opponent.textContent = entry.opponent;
    const score = document.createElement("span");
    score.textContent = `Điểm ${entry.scoreX}–${entry.scoreO} · Ván ${entry.round}`;
    const date = document.createElement("time");
    date.dateTime = entry.playedAt;
    date.textContent = new Intl.DateTimeFormat("vi-VN", {
      dateStyle: "short",
      timeStyle: "short"
    }).format(new Date(entry.playedAt));
    details.append(opponent, score, date);
    card.append(result, details);
    historyList.appendChild(card);
  }
}

function recordFinishedGame(state) {
  if (!state.winner || !activeRoomCode) return;
  const id = `${activeRoomCode}:${state.turnFinishedAt ?? `${state.round}:${state.winner}`}`;
  if (recordedGameIds.has(id)) return;
  recordedGameIds.add(id);

  gameHistory.unshift({
    id,
    playedAt: state.turnFinishedAt || new Date().toISOString(),
    result: state.winner === "draw" ? "draw" : state.winner === mySide ? "win" : "loss",
    opponent: isBotGame ? `Máy (${botDifficultyLabel})` : `Người chơi ${mySide === "X" ? "O" : "X"}`,
    scoreX: state.scoreX ?? 0,
    scoreO: state.scoreO ?? 0,
    round: state.round ?? 1
  });
  gameHistory = gameHistory.slice(0, maxHistoryEntries);
  saveGameHistory();
  renderGameHistory();
}

function showPage(page) {
  const showingHistory = page === "history";
  gamePage.hidden = showingHistory;
  historyPage.hidden = !showingHistory;
  document.getElementById("game-tab").classList.toggle("active", !showingHistory);
  document.getElementById("history-tab").classList.toggle("active", showingHistory);
}

function setConnected(connected) {
  connectionElement.textContent = connected ? "Đã kết nối" : "Mất kết nối";
  connectionElement.className = `connection ${connected ? "online" : "offline"}`;
  nicknameConnection.textContent = connected
    ? "Đã kết nối — bạn có thể tạo phòng hoặc nhập mã để tham gia."
    : "Chưa kết nối máy chủ — đang tự thử lại, vui lòng đợi một chút.";
  nicknameConnection.className = `nickname-connection ${connected ? "online" : "offline"}`;
  nicknameCreateButton.disabled = !connected;
  nicknameJoinButton.disabled = !connected || nicknameJoinCode.value.length !== 6;
  nicknameBotButton.disabled = !connected;
  document.getElementById("connection-note").textContent =
    connected ? "Kết nối ổn định" : "Đang thử kết nối lại";
  document.getElementById("create-room").disabled = !connected || mySide !== null;
  document.getElementById("join-room").disabled = !connected || mySide !== null;
  createBotButton.disabled = !connected || mySide !== null;
  botDifficulty.disabled = !connected || (mySide !== null && !isBotGame);
  leaveRoomButton.disabled = !connected || mySide === null;
}

function showNicknameError(message) {
  nicknameError.textContent = message;
  nicknameError.hidden = !message;
}

function updateNicknameDisplay(nickname) {
  playerNickname = nickname;
  document.getElementById("account-name").textContent = nickname || "Bạn";
  document.getElementById("account-avatar").textContent = Array.from(nickname || "B")[0].toUpperCase();
}

function getNicknameFromInput() {
  const nickname = nicknameInput.value.trim();
  if (!nickname)
    throw new Error("Hãy nhập tên hiển thị trước khi tiếp tục.");
  if (nickname.length > 20)
    throw new Error("Tên hiển thị không được dài quá 20 ký tự.");
  if (/[\u0000-\u001f\u007f]/.test(nickname))
    throw new Error("Tên hiển thị không được chứa ký tự điều khiển.");
  return nickname;
}

async function runRoomAction(button, method, ...args) {
  showNicknameError("");
  button.disabled = true;
  try {
    const nickname = getNicknameFromInput();
    nicknameInput.value = nickname;
    updateNicknameDisplay(nickname);
    try {
      sessionStorage.setItem(nicknameStorageKey, nickname);
    } catch (error) {
      showNicknameError(`Không lưu được tên cho lần tải lại tiếp theo: ${error.message}`);
    }

    if (connection.state !== signalR.HubConnectionState.Connected)
      throw new Error("Đang kết nối máy chủ. Vui lòng thử lại sau giây lát.");

    await connection.invoke("SetNickname", nickname);
    await connection.invoke(method, ...args);
  } catch (error) {
    showNicknameError(error.message);
    if (!gameScreen.hidden)
      appendMessage(error.message, "system");
  } finally {
    setConnected(connection.state === signalR.HubConnectionState.Connected);
  }
}

function showGameScreen() {
  if (!gameScreen.hidden) return;
  gameScreen.hidden = false;
  gameScreen.classList.add("screen-entering");
  requestAnimationFrame(() => {
    nicknameScreen.classList.add("screen-leaving");
    gameScreen.classList.remove("screen-entering");
  });
  window.setTimeout(() => {
    nicknameScreen.hidden = true;
    nicknameScreen.classList.remove("screen-leaving");
  }, 240);
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

function rollbackPendingMove(pending, message) {
  if (pendingMove !== pending) return;
  pendingMove = null;
  renderBoard(pending.previousState);
  if (message) appendMessage(message, "system");
}

async function makeMoveOptimistically(row, column) {
  if (!currentState || pendingMove || !mySide ||
      connection.state !== signalR.HubConnectionState.Connected)
    return;

  const previousState = currentState;
  if (!previousState.hasOpponent || previousState.winner ||
      previousState.nextTurn !== mySide || previousState.board[row][column] !== ".")
    return;

  const pending = { row, column, previousState };
  pendingMove = pending;
  const board = previousState.board.map((line) => line.split(""));
  board[row][column] = mySide;
  renderBoard({
    ...previousState,
    board: board.map((line) => line.join("")),
    nextTurn: mySide === "X" ? "O" : "X",
    lastRow: row,
    lastColumn: column
  });

  try {
    await connection.invoke("MakeMove", row, column);
  } catch (error) {
    rollbackPendingMove(pending, `Không gửi được nước đi: ${error.message}`);
  }
}

function storePlayerToken(token) {
  playerToken = token;
  try {
    sessionStorage.setItem(playerTokenStorageKey, token);
  } catch (error) {
    appendMessage(`Không lưu được mã khôi phục trong phiên này: ${error.message}`, "system");
  }
}

function clearPlayerToken() {
  playerToken = null;
  try {
    sessionStorage.removeItem(playerTokenStorageKey);
  } catch (error) {
    appendMessage(`Không xóa được mã phiên khỏi trình duyệt: ${error.message}`, "system");
  }
}

async function applySavedNickname() {
  if (playerNickname && connection.state === signalR.HubConnectionState.Connected)
    await connection.invoke("SetNickname", playerNickname);
}

async function rejoinStoredRoom() {
  if (!playerToken || rejoinInProgress ||
      connection.state !== signalR.HubConnectionState.Connected)
    return;

  rejoinInProgress = true;
  try {
    await connection.invoke("Rejoin", playerToken);
  } catch (error) {
    appendMessage(`Không khôi phục được phòng: ${error.message}`, "system");
  } finally {
    rejoinInProgress = false;
  }
}

function scheduleConnectionStart() {
  if (reconnectTimer !== null) return;
  reconnectTimer = window.setTimeout(() => {
    reconnectTimer = null;
    start();
  }, 3000);
}

function renderBoard(state) {
  currentState = state;
  boardElement.replaceChildren();
  const opponentConnected = isBotGame || (mySide === "X"
    ? state.guestConnected !== false
    : state.hostConnected !== false);
  const canPlay = Boolean(mySide && state.hasOpponent && opponentConnected &&
    connection.state === signalR.HubConnectionState.Connected &&
    !pendingMove && !state.hasPendingRequest && !state.winner && state.nextTurn === mySide);
  const liveBadge = document.querySelector(".live-badge");
  liveBadge.innerHTML = "";
  const badgeDot = document.createElement("span");
  liveBadge.append(badgeDot, document.createTextNode(
    !mySide ? " SẴN SÀNG" : !state.hasOpponent ? " ĐANG CHỜ" :
      !opponentConnected ? " ĐANG KẾT NỐI LẠI" :
        state.winner ? " ĐÃ KẾT THÚC" : " ĐANG DIỄN RA"
  ));

  for (let row = 0; row < 15; row++) {
    for (let column = 0; column < 15; column++) {
      const mark = state.board[row][column];
      const cell = document.createElement("button");
      cell.type = "button";
      cell.className = `cell ${mark === "." ? "" : mark.toLowerCase()}`.trim();
      if (state.lastRow === row && state.lastColumn === column)
        cell.classList.add("last-move");
      if (mark !== "." && state.lastRow === row && state.lastColumn === column)
        cell.classList.add("just-played");
      cell.textContent = mark === "." ? "" : mark;
      cell.setAttribute("aria-label", `Hàng ${row + 1}, cột ${column + 1}${mark === "." ? "" : `, quân ${mark}`}`);
      cell.disabled = !canPlay || mark !== ".";
      cell.addEventListener("click", () => makeMoveOptimistically(row, column));
      boardElement.appendChild(cell);
    }
  }

  document.getElementById("name-x").textContent = mySide === "X"
    ? playerNickname || "Bạn"
    : state.nicknameX || (state.hasOpponent ? "Đối thủ" : "Người chơi X");
  document.getElementById("name-o").textContent = mySide === "O"
    ? playerNickname || "Bạn"
    : isBotGame ? `Máy (${botDifficultyLabel})`
      : state.nicknameO || (state.hasOpponent ? "Đối thủ" : "Đang chờ");
  document.getElementById("avatar-x").textContent = "X";
  document.getElementById("avatar-o").textContent = state.hasOpponent ? "O" : "…";
  document.getElementById("score-x").textContent = String(state.scoreX ?? 0);
  document.getElementById("score-o").textContent = String(state.scoreO ?? 0);
  document.getElementById("round-label").textContent = `VÁN ${Math.min(state.round ?? 1, 3)}/3`;
  document.getElementById("online-count").textContent = isBotGame ? "1" :
    state.hasOpponent && opponentConnected ? "2" : "1";
  document.getElementById("player-card-x").classList.toggle("active-turn", state.hasOpponent && !state.winner && state.nextTurn === "X");
  document.getElementById("player-card-o").classList.toggle("active-turn", state.hasOpponent && !state.winner && state.nextTurn === "O");

  if (!state.hasOpponent) {
    statusElement.textContent = mySide ? "Đang chờ người chơi thứ hai..." : "Tạo phòng để bắt đầu";
    document.getElementById("turn-hint").textContent = "Gửi mã phòng cho bạn bè để bắt đầu";
  } else if (!opponentConnected) {
    statusElement.textContent = "Đối thủ đang kết nối lại...";
    document.getElementById("turn-hint").textContent = "Phòng được giữ trong 45 giây để người chơi quay lại";
  } else if (state.winner === "draw") {
    statusElement.textContent = "Ván đấu hòa";
    document.getElementById("turn-hint").textContent = "Chơi lại để bắt đầu ván tiếp theo";
  } else if (state.winner) {
    statusElement.textContent = state.winner === mySide ? "Bạn đã chiến thắng!" : `Quân ${state.winner} chiến thắng`;
    document.getElementById("turn-hint").textContent = "Ván đấu đã kết thúc";
  } else if (isBotGame && state.nextTurn === "O") {
    statusElement.textContent = "Máy đang suy nghĩ...";
    document.getElementById("turn-hint").textContent = "Máy đang chọn nước đi...";
  } else {
    statusElement.textContent = state.nextTurn === mySide ? "Lượt của bạn" : `Đang chờ quân ${state.nextTurn}`;
    document.getElementById("turn-hint").textContent = state.nextTurn === mySide ? "Đến lượt bạn chọn nước đi" : "Đối thủ đang suy nghĩ";
  }
  statusElement.classList.toggle("thinking", isBotGame && state.nextTurn === "O" && !state.winner);

  document.getElementById("turn-icon").textContent = state.hasOpponent ? state.nextTurn : "X";
  document.getElementById("turn-label-x").textContent = state.hasOpponent && !state.winner && state.nextTurn === "X" ? "ĐANG ĐI" : "QUÂN X";
  document.getElementById("turn-label-o").textContent = state.hasOpponent && !state.winner && state.nextTurn === "O" ? "ĐANG ĐI" : state.hasOpponent ? "QUÂN O" : "ĐANG CHỜ";
  const turnLiveDetail = document.getElementById("turn-live-detail");
  turnLiveDetail.className = "turn-live-detail";
  if (!state.hasOpponent) {
    turnLiveDetail.textContent = "Đang chờ đối thủ tham gia phòng.";
    turnLiveDetail.classList.add("turn-waiting");
  } else if (!opponentConnected) {
    turnLiveDetail.textContent = "Đối thủ mất kết nối — đang chờ quay lại (tối đa 45 giây).";
    turnLiveDetail.classList.add("turn-waiting");
  } else if (state.winner) {
    turnLiveDetail.textContent = "Ván đấu đã kết thúc.";
    turnLiveDetail.classList.add("turn-waiting");
  } else if (state.nextTurn === mySide) {
    turnLiveDetail.textContent = `Đến lượt của bạn (Quân ${mySide}).`;
    turnLiveDetail.classList.add("turn-mine");
  } else {
    turnLiveDetail.textContent = `Đến lượt đối phương (Quân ${state.nextTurn}).`;
    turnLiveDetail.classList.add("turn-opponent");
  }
  undoButton.disabled = !state.hasOpponent || isBotGame || !opponentConnected ||
    !state.canUndo || connection.state !== signalR.HubConnectionState.Connected ||
    Boolean(pendingMove) || state.hasPendingRequest || pendingRequest !== null;
  restartButton.disabled = !state.hasOpponent || !opponentConnected ||
    connection.state !== signalR.HubConnectionState.Connected ||
    state.hasPendingRequest || pendingRequest !== null;
  document.getElementById("game-over-restart").disabled = restartButton.disabled;
  forfeitButton.disabled = !mySide || !state.hasOpponent || !opponentConnected ||
    connection.state !== signalR.HubConnectionState.Connected || Boolean(state.winner) ||
    Boolean(pendingMove) || state.hasPendingRequest;
  chatInput.disabled = !state.hasOpponent || !opponentConnected || isBotGame ||
    connection.state !== signalR.HubConnectionState.Connected;
  document.getElementById("send-chat").disabled = !state.hasOpponent || !opponentConnected ||
    isBotGame || connection.state !== signalR.HubConnectionState.Connected;
  updateTurnClock();
  renderGameOver(state);
}

function renderGameOver(state) {
  const dialog = document.getElementById("game-over-dialog");
  if (!state.winner) {
    shownGameOverKey = null;
    if (dialog.open) dialog.close();
    return;
  }

  const key = `${activeRoomCode}:${state.round}:${state.turnFinishedAt ?? state.winner}`;
  if (shownGameOverKey === key) return;
  shownGameOverKey = key;
  document.getElementById("game-over-message").textContent = state.winner === "draw"
    ? "🤝 Ván đấu hòa!"
    : `🏆 Người chơi ${state.winner} đã giành chiến thắng!`;
  if (!dialog.open) dialog.showModal();
}

function closeRequestPrompt() {
  const dialog = document.getElementById("request-dialog");
  if (dialog.open) dialog.close();
  activeRequestPrompt = null;
}

function showRequestPrompt(type, side) {
  if (activeRequestPrompt !== null) return;
  activeRequestPrompt = type;
  document.getElementById("request-dialog-title").textContent =
    type === "undo" ? "Xin xác nhận hoàn tác" : "Xin xác nhận chơi lại";
  document.getElementById("request-dialog-message").textContent =
    type === "undo"
      ? `Người chơi quân ${side} đề nghị lùi lại một nước cờ. Bạn có đồng ý không?`
      : `Người chơi quân ${side} muốn bắt đầu ván mới. Bạn có đồng ý không?`;
  document.getElementById("request-dialog").showModal();
}

async function respondToRequest(accepted) {
  const type = activeRequestPrompt;
  if (!type) return;
  closeRequestPrompt();
  try {
    await connection.invoke(type === "undo" ? "RespondUndo" : "RespondRestart", accepted);
  } catch (error) {
    appendMessage(`Không phản hồi được yêu cầu: ${error.message}`, "system");
  }
}

function clearLocalRoom(status, details) {
  pendingMove = null;
  pendingRequest = null;
  closeRequestPrompt();
  const gameOverDialog = document.getElementById("game-over-dialog");
  if (gameOverDialog.open) gameOverDialog.close();
  shownGameOverKey = null;
  mySide = null;
  isBotGame = false;
  botDifficultyLabel = "";
  activeRoomCode = null;
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
  undoButton.disabled = true;
  forfeitButton.disabled = true;
  leaveRoomButton.disabled = true;
  chatInput.disabled = true;
  document.getElementById("send-chat").disabled = true;
  setConnected(connection.state === signalR.HubConnectionState.Connected);
}

nicknameInput.value = playerNickname;
updateNicknameDisplay(playerNickname);
nicknameCreateButton.addEventListener("click", () =>
  runRoomAction(nicknameCreateButton, "CreateRoom"));
nicknameJoinButton.addEventListener("click", () => {
  const code = nicknameJoinCode.value.trim();
  if (code.length !== 6) {
    showNicknameError("Mã phòng phải gồm 6 ký tự.");
    return;
  }
  runRoomAction(nicknameJoinButton, "JoinRoom", code);
});
nicknameBotButton.addEventListener("click", () => {
  botDifficulty.value = nicknameBotDifficulty.value;
  runRoomAction(nicknameBotButton, "StartBotGame", nicknameBotDifficulty.value);
});
nicknameInput.addEventListener("input", () => showNicknameError(""));
nicknameInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter")
    nicknameCreateButton.click();
});
nicknameJoinCode.addEventListener("input", () => {
  nicknameJoinCode.value = nicknameJoinCode.value.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6);
  nicknameJoinButton.disabled =
    connection.state !== signalR.HubConnectionState.Connected || nicknameJoinCode.value.length !== 6;
  showNicknameError("");
});
nicknameJoinCode.addEventListener("keydown", (event) => {
  if (event.key === "Enter")
    nicknameJoinButton.click();
});

document.getElementById("create-room").addEventListener("click", () =>
  runRoomAction(document.getElementById("create-room"), "CreateRoom"));

document.getElementById("game-tab").addEventListener("click", () => showPage("game"));
document.getElementById("history-tab").addEventListener("click", () => showPage("history"));
document.getElementById("clear-history").addEventListener("click", () => {
  gameHistory = [];
  recordedGameIds.clear();
  saveGameHistory();
  renderGameHistory();
});

createBotButton.addEventListener("click", () =>
  runRoomAction(createBotButton, "StartBotGame", botDifficulty.value));

document.getElementById("join-room").addEventListener("click", () =>
  runRoomAction(document.getElementById("join-room"), "JoinRoom", roomCodeInput.value));

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
  if (pendingRequest) return;
  try {
    pendingRequest = "restart";
    restartButton.disabled = true;
    undoButton.disabled = true;
    await connection.invoke("RequestRestart");
    if (isBotGame) {
      pendingRequest = null;
      if (currentState) renderBoard(currentState);
    }
  } catch (error) {
    pendingRequest = null;
    appendMessage(`Không gửi được yêu cầu: ${error.message}`, "system");
  }
});

undoButton.addEventListener("click", async () => {
  if (pendingRequest) return;
  try {
    pendingRequest = "undo";
    restartButton.disabled = true;
    undoButton.disabled = true;
    await connection.invoke("RequestUndo");
  } catch (error) {
    pendingRequest = null;
    appendMessage(`Không gửi được yêu cầu hoàn tác: ${error.message}`, "system");
  }
});

document.getElementById("request-accept").addEventListener("click", () => respondToRequest(true));
document.getElementById("request-decline").addEventListener("click", () => respondToRequest(false));
document.getElementById("request-dialog").addEventListener("cancel", (event) => {
  event.preventDefault();
  respondToRequest(false);
});
document.getElementById("game-over-close").addEventListener("click", () => {
  document.getElementById("game-over-dialog").close();
});
document.getElementById("game-over-restart").addEventListener("click", async () => {
  document.getElementById("game-over-dialog").close();
  restartButton.click();
});

forfeitButton.addEventListener("click", async () => {
  if (!window.confirm("Bạn chắc chắn muốn xin thua ván này?")) return;
  try {
    await connection.invoke("Forfeit");
  } catch (error) {
    appendMessage(`Không gửi được yêu cầu xin thua: ${error.message}`, "system");
  }
});

leaveRoomButton.addEventListener("click", async () => {
  if (!window.confirm("Rời phòng hiện tại? Ván chưa kết thúc sẽ không được tính kết quả.")) return;
  leaveRoomButton.disabled = true;
  try {
    await connection.invoke("LeaveRoom");
    clearPlayerToken();
    roomCodeInput.value = "";
    clearLocalRoom("Đã rời phòng", "Tạo phòng mới hoặc nhập mã của người chơi khác.");
  } catch (error) {
    appendMessage(`Không rời phòng được: ${error.message}`, "system");
    leaveRoomButton.disabled = false;
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

connection.on("RoomCreated", (code, side, token) => {
  showGameScreen();
  mySide = side;
  isBotGame = false;
  botDifficultyLabel = "";
  activeRoomCode = code;
  storePlayerToken(token);
  roomCodeInput.value = code;
  roomCodeDisplay.textContent = code;
  copyButton.disabled = false;
  appendMessage(`Đã tạo phòng ${code}. Gửi mã này cho bạn bè để cùng chơi.`, "system");
  setConnected(connection.state === signalR.HubConnectionState.Connected);
});

connection.on("JoinedRoom", (code, side, token) => {
  showGameScreen();
  mySide = side;
  isBotGame = false;
  botDifficultyLabel = "";
  activeRoomCode = code;
  storePlayerToken(token);
  roomCodeInput.value = code;
  roomCodeDisplay.textContent = code;
  copyButton.disabled = false;
  appendMessage(`Bạn đã vào phòng ${code}.`, "system");
  setConnected(connection.state === signalR.HubConnectionState.Connected);
});

connection.on("BotGameStarted", (code, side, difficulty, token) => {
  showGameScreen();
  mySide = side;
  isBotGame = true;
  botDifficultyLabel = { easy: "dễ", medium: "trung bình", hard: "khó" }[difficulty] ?? difficulty;
  activeRoomCode = code;
  storePlayerToken(token);
  botDifficulty.value = difficulty;
  nicknameBotDifficulty.value = difficulty;
  roomCodeInput.value = "";
  roomCodeDisplay.textContent = "VS MÁY";
  copyButton.disabled = true;
  appendMessage(`Bắt đầu ván với máy ở mức ${botDifficultyLabel}.`, "system");
  setConnected(connection.state === signalR.HubConnectionState.Connected);
});

botDifficulty.addEventListener("change", async () => {
  if (!isBotGame) return;
  try {
    await connection.invoke("SetBotDifficulty", botDifficulty.value);
  } catch (error) {
    appendMessage(`Không đổi được độ khó bot: ${error.message}`, "system");
  }
});

connection.on("BotDifficultyChanged", (_code, difficulty) => {
  botDifficulty.value = difficulty;
  botDifficultyLabel = { easy: "dễ", medium: "trung bình", hard: "khó" }[difficulty] ?? difficulty;
  appendMessage(`Độ khó bot đã đổi thành ${botDifficultyLabel}.`, "system");
  if (currentState) renderBoard(currentState);
});

connection.on("GameState", (state) => {
  const previousNextTurn = pendingMove?.previousState.nextTurn ?? currentState?.nextTurn;
  const hadOpponent = currentState?.hasOpponent;
  pendingMove = null;
  recordFinishedGame(state);
  renderBoard(state);
  if (hadOpponent && previousNextTurn !== state.nextTurn && state.hasOpponent && !state.winner)
    playMoveSound();
});
connection.on("RoomRejoined", (code, side, botGame, difficulty, token) => {
  showGameScreen();
  mySide = side;
  isBotGame = botGame;
  botDifficultyLabel = botGame
    ? ({ easy: "dễ", medium: "trung bình", hard: "khó" }[difficulty] ?? difficulty)
    : "";
  activeRoomCode = code;
  storePlayerToken(token);
  roomCodeInput.value = botGame ? "" : code;
  roomCodeDisplay.textContent = botGame ? "VS MÁY" : code;
  copyButton.disabled = botGame;
  if (botGame) botDifficulty.value = difficulty;
  if (botGame) nicknameBotDifficulty.value = difficulty;
  setConnected(true);
  appendMessage(`Đã kết nối lại phòng ${botGame ? "đấu với máy" : code}.`, "system");
});
connection.on("RejoinFailed", (text) => {
  clearPlayerToken();
  pendingMove = null;
  roomCodeInput.value = "";
  clearLocalRoom("Không thể khôi phục phòng", text);
  showNicknameError(text);
  appendMessage(text, "system");
});
connection.on("SessionReplaced", () => {
  clearPlayerToken();
  clearLocalRoom("Phiên chơi đã được mở ở nơi khác", "Tạo phòng mới hoặc tham gia lại bằng mã phòng.");
});
connection.on("MoveRejected", (row, column, text) => {
  if (pendingMove && pendingMove.row === row && pendingMove.column === column)
    rollbackPendingMove(pendingMove, text);
  else
    appendMessage(text, "system");
});
connection.on("ErrorMessage", (text) => {
  pendingRequest = null;
  if (currentState) renderBoard(currentState);
  if (gameScreen.hidden)
    showNicknameError(text);
  appendMessage(text, "system");
});
connection.on("SystemMessage", (text) => appendMessage(text, "system"));
connection.on("ChatMessage", (side, text) => appendMessage(text, "", side));
connection.on("PlayerDisconnected", (side, timeoutSeconds) => {
  const who = side === mySide ? "Bạn" : "Đối thủ";
  appendMessage(`${who} mất kết nối. Phòng được giữ trong ${timeoutSeconds} giây để kết nối lại.`, "system");
});
connection.on("PlayerRejoined", (side) => {
  if (side !== mySide)
    appendMessage("Đối thủ đã kết nối lại.", "system");
});
connection.on("OpponentDisconnected", () => {
  appendMessage("Đối thủ đã rời phòng hoặc hết thời gian kết nối lại.", "system");
});
connection.on("RoomClosed", () => {
  appendMessage("Người tạo phòng đã rời đi; phòng đã đóng.", "system");
  connection.invoke("LeaveRoom").catch((error) => appendMessage(`Lỗi rời phòng: ${error.message}`, "system"));
  clearPlayerToken();
  roomCodeInput.value = "";
  activeRoomCode = null;
  clearLocalRoom("Phòng đã đóng", "Tạo phòng mới hoặc nhập mã của bạn bè.");
});
connection.on("RestartRequested", (side) => showRequestPrompt("restart", side));
connection.on("UndoRequested", (side) => showRequestPrompt("undo", side));
connection.on("RestartDeclined", () => {
  pendingRequest = null;
  if (currentState) renderBoard(currentState);
  appendMessage("Đối thủ từ chối chơi lại.", "system");
});
connection.on("UndoDeclined", () => {
  pendingRequest = null;
  if (currentState) renderBoard(currentState);
  appendMessage("Đối thủ từ chối hoàn tác.", "system");
});
connection.on("RequestAccepted", (requestType) => {
  pendingRequest = null;
  appendMessage(
    requestType === "undo" ? "Yêu cầu hoàn tác đã được chấp nhận." : "Yêu cầu chơi lại đã được chấp nhận.",
    "system");
});
connection.on("RequestCancelled", () => {
  pendingRequest = null;
  closeRequestPrompt();
  if (currentState) renderBoard(currentState);
  appendMessage("Yêu cầu đã bị hủy do một người chơi mất kết nối.", "system");
});
connection.onreconnecting(() => {
  pendingRequest = null;
  closeRequestPrompt();
  connectionElement.textContent = "Đang kết nối lại...";
  connectionElement.className = "connection";
  document.getElementById("connection-note").textContent = "Đang khôi phục kết nối và phòng chơi";
  if (currentState) renderBoard(currentState);
  statusElement.textContent = "Đang kết nối lại...";
  document.getElementById("turn-hint").textContent = "Đang thử khôi phục phiên chơi";
});
connection.onreconnected(async () => {
  setConnected(true);
  await applySavedNickname();
  await rejoinStoredRoom();
});
connection.onclose(() => {
  setConnected(false);
  if (currentState) renderBoard(currentState);
  statusElement.textContent = "Mất kết nối máy chủ";
  document.getElementById("turn-hint").textContent = "Đang thử kết nối lại và khôi phục phòng...";
  scheduleConnectionStart();
});

async function start() {
  if (connection.state !== signalR.HubConnectionState.Disconnected) return;
  try {
    await connection.start();
    setConnected(true);
    await applySavedNickname();
    await rejoinStoredRoom();
  } catch (error) {
    setConnected(false);
    appendMessage(`Không kết nối được máy chủ: ${error.message}`, "system");
    scheduleConnectionStart();
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
gameHistory = loadGameHistory();
renderGameHistory();
start();
